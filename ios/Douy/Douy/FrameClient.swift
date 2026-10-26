import Foundation
import Network
import UIKit

/// 与 Windows 端 DouyProtocol 相同：大端，magic DOUY，type 4 为 H.264。
final class FrameClient: ObservableObject {
    @Published var status = "未连接"
    @Published var fps = 0.0
    @Published var frameWidth = 0
    @Published var frameHeight = 0
    @Published var isConnected = false
    @Published var hasPicture = false

    let player = H264Player()

    private let queue = DispatchQueue(label: "app.douy.frame")
    private var connection: NWConnection?
    private var buffer = Data()
    private var readOffset = 0
    private var frameTimes: [CFAbsoluteTime] = []
    private var userClosed = false
    private var session = 0

    func report(_ text: String) {
        DispatchQueue.main.async {
            self.status = text
        }
    }

    func connect(host: String, port: UInt16) {
        userClosed = false
        session += 1
        let session = session
        connection?.cancel()
        connection = nil
        player.reset()
        hasPicture = false
        frameWidth = 0
        frameHeight = 0
        fps = 0
        queue.async { [weak self] in
            guard let self, self.session == session else { return }
            self.buffer.removeAll()
            self.readOffset = 0
        }
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        tcp.connectionTimeout = 5
        let parameters = NWParameters(tls: nil, tcp: tcp)
        let connection = NWConnection(host: NWEndpoint.Host(host), port: NWEndpoint.Port(rawValue: port)!, using: parameters)
        self.connection = connection
        connection.stateUpdateHandler = { [weak self] state in
            guard let self, self.session == session else { return }
            self.handle(state)
        }
        status = "正在连接 \(host):\(port)"
        connection.start(queue: queue)
    }

    func disconnect(userInitiated: Bool = true) {
        userClosed = userInitiated
        session += 1
        connection?.cancel()
        connection = nil
        player.reset()
        let session = session
        queue.async { [weak self] in
            guard let self, self.session == session else { return }
            self.buffer.removeAll()
            self.readOffset = 0
        }
        isConnected = false
        hasPicture = false
        fps = 0
        UIApplication.shared.isIdleTimerDisabled = false
        if userInitiated {
            status = "已断开"
        }
    }

    private func handle(_ state: NWConnection.State) {
        DispatchQueue.main.async {
            switch state {
            case .ready:
                self.isConnected = true
                self.status = "已连接，等待画面"
                UIApplication.shared.isIdleTimerDisabled = true
                self.sendHello()
                self.receive()
            case .waiting(let error):
                self.status = "连不上：\(error.localizedDescription)。确认电脑已点开始传输，并且手机和电脑在同一路由器。"
            case .failed(let error):
                self.isConnected = false
                self.status = "连接失败：\(error.localizedDescription)"
                UIApplication.shared.isIdleTimerDisabled = false
            case .cancelled:
                self.isConnected = false
                UIApplication.shared.isIdleTimerDisabled = false
                if !self.userClosed {
                    self.status = "连接已断开"
                }
            default:
                break
            }
        }
    }

    private func sendHello() {
        let payload = Array(UIDevice.current.name.utf8)
        var packet = header(type: 1, payloadLength: payload.count)
        packet.append(contentsOf: payload)
        connection?.send(content: packet, completion: .contentProcessed { _ in })
    }

    private func receive() {
        guard let connection else { return }
        connection.receive(minimumIncompleteLength: 1, maximumLength: 256 * 1024) { [weak self] data, _, isComplete, error in
            guard let self, self.connection === connection else { return }
            if let data, !data.isEmpty {
                self.buffer.append(data)
                self.drain()
            }
            if isComplete || error != nil {
                DispatchQueue.main.async {
                    guard self.connection === connection else { return }
                    self.isConnected = false
                    self.status = "连接已断开"
                    UIApplication.shared.isIdleTimerDisabled = false
                }
                return
            }
            self.receive()
        }
    }

    private func drain() {
        let headerSize = 10
        while buffer.count - readOffset >= headerSize {
            let start = readOffset
            let magic = readUInt32(at: start)
            let version = buffer[buffer.startIndex + start + 4]
            if magic != 0x444F5559 || version != 1 {
                readOffset += 1
                continue
            }

            let type = buffer[buffer.startIndex + start + 5]
            let length = Int(readUInt32(at: start + 6))
            if length > 8 * 1024 * 1024 {
                readOffset += 1
                continue
            }

            let total = headerSize + length
            if buffer.count - readOffset < total {
                break
            }

            if type == 4, length >= 17 {
                let payload = start + headerSize
                let width = Int(readUInt16(at: payload + 4))
                let height = Int(readUInt16(at: payload + 6))
                let pts = readUInt64(at: payload + 8)
                let dataStart = buffer.startIndex + payload + 17
                let dataEnd = buffer.startIndex + start + total
                let accessUnit = buffer.subdata(in: dataStart..<dataEnd)
                let token = session
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.session == token else { return }
                    self.player.submit(accessUnit, ptsMilliseconds: pts)
                    self.frameWidth = width
                    self.frameHeight = height
                    self.noteFrame()
                    self.hasPicture = true
                    self.status = "正在播放"
                }
            }

            readOffset += total
        }

        if readOffset > 256 * 1024 {
            buffer.removeFirst(readOffset)
            readOffset = 0
        }
    }

    private func noteFrame() {
        let now = CFAbsoluteTimeGetCurrent()
        frameTimes.append(now)
        frameTimes.removeAll { now - $0 > 1 }
        fps = Double(frameTimes.count)
    }

    private func readUInt32(at offset: Int) -> UInt32 {
        let base = buffer.startIndex + offset
        return (UInt32(buffer[base]) << 24)
            | (UInt32(buffer[base + 1]) << 16)
            | (UInt32(buffer[base + 2]) << 8)
            | UInt32(buffer[base + 3])
    }

    private func readUInt16(at offset: Int) -> UInt16 {
        let base = buffer.startIndex + offset
        return (UInt16(buffer[base]) << 8) | UInt16(buffer[base + 1])
    }

    private func readUInt64(at offset: Int) -> UInt64 {
        let base = buffer.startIndex + offset
        var value: UInt64 = 0
        for shift in 0..<8 {
            value = (value << 8) | UInt64(buffer[base + shift])
        }
        return value
    }

    private func header(type: UInt8, payloadLength: Int) -> Data {
        var data = Data(count: 10)
        let length = UInt32(payloadLength)
        data[0] = 0x44
        data[1] = 0x4F
        data[2] = 0x55
        data[3] = 0x59
        data[4] = 1
        data[5] = type
        data[6] = UInt8((length >> 24) & 0xFF)
        data[7] = UInt8((length >> 16) & 0xFF)
        data[8] = UInt8((length >> 8) & 0xFF)
        data[9] = UInt8(length & 0xFF)
        return data
    }
}
