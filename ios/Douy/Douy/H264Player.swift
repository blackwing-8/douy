import AVFoundation
import CoreMedia
import UIKit

final class H264HostView: UIView {
    override class var layerClass: AnyClass { AVSampleBufferDisplayLayer.self }
    var playerLayer: AVSampleBufferDisplayLayer { layer as! AVSampleBufferDisplayLayer }
}

/// 把 Annex-B 的 H.264 按时间戳交给系统解码器，突发到达的帧会排队匀速播放。
final class H264Player {
    private let queue = DispatchQueue(label: "app.douy.h264")
    private weak var layer: AVSampleBufferDisplayLayer?
    private var format: CMFormatDescription?
    private var timebase: CMTimebase?
    private var sps = Data()
    private var pps = Data()
    private var pending: [(Data, CMTime)] = []

    func attach(_ layer: AVSampleBufferDisplayLayer) {
        queue.async { [weak self] in
            guard let self else { return }
            self.layer = layer
            let waiting = self.pending
            self.pending.removeAll()
            for item in waiting {
                self.enqueue(item.0, pts: item.1, layer: layer)
            }
        }
    }

    func reset() {
        queue.async { [weak self] in
            guard let self else { return }
            self.format = nil
            self.timebase = nil
            self.sps.removeAll()
            self.pps.removeAll()
            self.pending.removeAll()
            let layer = self.layer
            DispatchQueue.main.async {
                layer?.flushAndRemoveImage()
            }
        }
    }

    func submit(_ accessUnit: Data, ptsMilliseconds: UInt64) {
        let pts = CMTime(value: CMTimeValue(ptsMilliseconds), timescale: 1000)
        let copy = accessUnit
        queue.async { [weak self] in
            guard let self else { return }
            guard let layer = self.layer else {
                self.pending.append((copy, pts))
                if self.pending.count > 45 {
                    self.pending.removeFirst(self.pending.count - 45)
                }
                return
            }
            self.enqueue(copy, pts: pts, layer: layer)
        }
    }

    private func enqueue(_ accessUnit: Data, pts: CMTime, layer: AVSampleBufferDisplayLayer) {
        let nals = H264Player.nalPayloads(accessUnit)
        guard !nals.isEmpty else { return }

        var picture = Data()
        var sawParameterSet = false
        for nal in nals {
            let kind = nal[0] & 0x1F
            if kind == 7 {
                if nal != sps {
                    sps = nal
                    sawParameterSet = true
                }
                continue
            }
            if kind == 8 {
                if nal != pps {
                    pps = nal
                    sawParameterSet = true
                }
                continue
            }
            if kind == 9 {
                continue
            }

            var length = UInt32(nal.count).bigEndian
            withUnsafeBytes(of: &length) { picture.append(contentsOf: $0) }
            picture.append(nal)
        }

        if sawParameterSet || format == nil {
            guard !sps.isEmpty, !pps.isEmpty, let created = makeFormat(sps: sps, pps: pps) else { return }
            format = created
            let display = layer
            DispatchQueue.main.async {
                display.flush()
            }
        }

        guard let format, !picture.isEmpty else { return }
        guard let sample = makeSample(picture, format: format, pts: pts) else { return }
        ensureTimebase(startingAt: pts)
        if shouldDisplayNow(pts) {
            markImmediate(sample)
        }

        let display = layer
        let base = timebase
        let isIdr = nals.contains { !$0.isEmpty && ($0[0] & 0x1F) == 5 }
        DispatchQueue.main.async {
            if display.controlTimebase == nil {
                display.controlTimebase = base
            }
            if display.status == .failed {
                display.flush()
            }
            if !display.isReadyForMoreMediaData && isIdr {
                display.flush()
            }
            if display.isReadyForMoreMediaData || isIdr {
                display.enqueue(sample)
            }
        }
    }

    private func shouldDisplayNow(_ pts: CMTime) -> Bool {
        guard let timebase else { return false }
        let delta = CMTimeGetSeconds(CMTimeSubtract(pts, CMTimebaseGetTime(timebase)))
        if delta < -0.08 || delta > 0.5 {
            CMTimebaseSetTime(timebase, time: pts)
            return true
        }
        return false
    }

    private func ensureTimebase(startingAt pts: CMTime) {
        if timebase != nil { return }
        var created: CMTimebase?
        let status = CMTimebaseCreateWithSourceClock(
            allocator: kCFAllocatorDefault,
            sourceClock: CMClockGetHostTimeClock(),
            timebaseOut: &created
        )
        guard status == noErr, let created else { return }
        CMTimebaseSetTime(created, time: pts)
        CMTimebaseSetRate(created, rate: 1)
        timebase = created
    }

    private func makeSample(_ avcc: Data, format: CMFormatDescription, pts: CMTime) -> CMSampleBuffer? {
        var block: CMBlockBuffer?
        guard CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault,
            memoryBlock: nil,
            blockLength: avcc.count,
            blockAllocator: kCFAllocatorDefault,
            customBlockSource: nil,
            offsetToData: 0,
            dataLength: avcc.count,
            flags: 0,
            blockBufferOut: &block
        ) == kCMBlockBufferNoErr, let block else { return nil }

        let copied = avcc.withUnsafeBytes { raw in
            CMBlockBufferReplaceDataBytes(
                with: raw.baseAddress!,
                blockBuffer: block,
                offsetIntoDestination: 0,
                dataLength: avcc.count
            )
        }
        guard copied == kCMBlockBufferNoErr else { return nil }

        var timing = CMSampleTimingInfo(duration: CMTime(value: 1, timescale: 30), presentationTimeStamp: pts, decodeTimeStamp: .invalid)
        var size = avcc.count
        var sample: CMSampleBuffer?
        let status = CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault,
            dataBuffer: block,
            formatDescription: format,
            sampleCount: 1,
            sampleTimingEntryCount: 1,
            sampleTimingArray: &timing,
            sampleSizeEntryCount: 1,
            sampleSizeArray: &size,
            sampleBufferOut: &sample
        )
        return status == noErr ? sample : nil
    }

    private func makeFormat(sps: Data, pps: Data) -> CMFormatDescription? {
        var description: CMFormatDescription?
        let status: OSStatus = sps.withUnsafeBytes { spsRaw in
            pps.withUnsafeBytes { ppsRaw in
                let pointers = [
                    spsRaw.bindMemory(to: UInt8.self).baseAddress!,
                    ppsRaw.bindMemory(to: UInt8.self).baseAddress!
                ]
                let sizes = [sps.count, pps.count]
                return CMVideoFormatDescriptionCreateFromH264ParameterSets(
                    allocator: kCFAllocatorDefault,
                    parameterSetCount: 2,
                    parameterSetPointers: pointers,
                    parameterSetSizes: sizes,
                    nalUnitHeaderLength: 4,
                    formatDescriptionOut: &description
                )
            }
        }
        return status == noErr ? description : nil
    }

    private func markImmediate(_ sample: CMSampleBuffer) {
        guard let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: true),
              CFArrayGetCount(attachments) > 0 else { return }
        let dictionary = Unmanaged<CFMutableDictionary>.fromOpaque(CFArrayGetValueAtIndex(attachments, 0)).takeUnretainedValue()
        CFDictionarySetValue(
            dictionary,
            Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
            Unmanaged.passUnretained(kCFBooleanTrue).toOpaque()
        )
    }

    private static func nalPayloads(_ data: Data) -> [Data] {
        let bytes = [UInt8](data)
        var ranges: [(Int, Int)] = []
        var index = 0
        while index + 3 < bytes.count {
            if index + 4 < bytes.count, bytes[index] == 0, bytes[index + 1] == 0, bytes[index + 2] == 0, bytes[index + 3] == 1 {
                ranges.append((index + 4, 4))
                index += 4
                continue
            }
            if bytes[index] == 0, bytes[index + 1] == 0, bytes[index + 2] == 1 {
                ranges.append((index + 3, 3))
                index += 3
                continue
            }
            index += 1
        }

        guard !ranges.isEmpty else { return lengthPrefixed(bytes) }
        var payloads: [Data] = []
        for cursor in ranges.indices {
            let begin = ranges[cursor].0
            let end: Int
            if cursor + 1 < ranges.count {
                end = ranges[cursor + 1].0 - ranges[cursor + 1].1
            } else {
                end = bytes.count
            }
            if end > begin {
                payloads.append(Data(bytes[begin..<end]))
            }
        }
        return payloads
    }

    private static func lengthPrefixed(_ bytes: [UInt8]) -> [Data] {
        var payloads: [Data] = []
        var index = 0
        while index + 4 <= bytes.count {
            let length = (Int(bytes[index]) << 24) | (Int(bytes[index + 1]) << 16) | (Int(bytes[index + 2]) << 8) | Int(bytes[index + 3])
            index += 4
            guard length > 0, index + length <= bytes.count else { return [] }
            payloads.append(Data(bytes[index..<(index + length)]))
            index += length
        }
        return payloads
    }
}
