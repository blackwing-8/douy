import SwiftUI

struct ContentView: View {
    @StateObject private var client = FrameClient()
    @State private var host = UserDefaults.standard.string(forKey: "douy.host") ?? ""
    @State private var port = UserDefaults.standard.string(forKey: "douy.port") ?? "19527"
    @State private var showControls = true

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if let image = client.image {
                Image(uiImage: image)
                    .resizable()
                    .scaledToFit()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .onTapGesture(count: 2) {
                        showControls.toggle()
                    }
            } else {
                VStack(spacing: 10) {
                    Text("兜鱼")
                        .font(.system(size: 44, weight: .bold))
                    Text("等待电脑画面")
                        .foregroundStyle(.white.opacity(0.7))
                }
                .foregroundStyle(.white)
            }

            if showControls {
                VStack {
                    statusBar
                    Spacer()
                    controlBar
                }
            }
        }
        .preferredColorScheme(.dark)
    }

    private var statusBar: some View {
        HStack {
            Text(client.status)
            Spacer()
            Text(stats)
        }
        .font(.footnote.monospacedDigit())
        .foregroundStyle(.white)
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.black.opacity(0.55))
    }

    private var stats: String {
        guard client.frameWidth > 0 else { return " " }
        return String(format: "%.0f fps  %d×%d", client.fps, client.frameWidth, client.frameHeight)
    }

    private var controlBar: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("填写电脑上兜鱼显示的地址。同一台路由器即可，两边 IP 不同没有关系。")
                .font(.footnote)
                .foregroundStyle(.white.opacity(0.75))

            HStack(spacing: 8) {
                TextField("电脑地址，例如 192.168.1.20", text: $host)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .keyboardType(.numbersAndPunctuation)
                    .padding(10)
                    .background(Color.white.opacity(0.08))
                    .clipShape(RoundedRectangle(cornerRadius: 8))

                TextField("端口", text: $port)
                    .keyboardType(.numberPad)
                    .frame(width: 84)
                    .padding(10)
                    .background(Color.white.opacity(0.08))
                    .clipShape(RoundedRectangle(cornerRadius: 8))
            }

            Button(action: toggleConnection) {
                Text(client.isConnected ? "断开" : "连接电脑")
                    .font(.headline)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 12)
                    .background(client.isConnected ? Color.white.opacity(0.14) : Color(red: 0.17, green: 0.71, blue: 0.45))
                    .foregroundStyle(client.isConnected ? Color.white : Color(red: 0.02, green: 0.13, blue: 0.09))
                    .clipShape(RoundedRectangle(cornerRadius: 10))
            }

            Text("双击画面可以隐藏这里。播放时请留在兜鱼里，不要切到后台。")
                .font(.caption)
                .foregroundStyle(.white.opacity(0.55))
        }
        .padding(16)
        .background(.ultraThinMaterial)
    }

    private func toggleConnection() {
        if client.isConnected {
            client.disconnect()
            return
        }

        var address = host.trimmingCharacters(in: .whitespacesAndNewlines)
        var portText = port.trimmingCharacters(in: .whitespacesAndNewlines)
        if let colon = address.lastIndex(of: ":"), address.firstIndex(of: ":") == colon {
            let maybePort = String(address[address.index(after: colon)...])
            let maybeHost = String(address[..<colon])
            if UInt16(maybePort) != nil, !maybeHost.isEmpty {
                address = maybeHost
                portText = maybePort
                host = maybeHost
                port = maybePort
            }
        }

        guard !address.isEmpty, let portNumber = UInt16(portText) else {
            client.report("请填写电脑地址和端口")
            return
        }

        UserDefaults.standard.set(address, forKey: "douy.host")
        UserDefaults.standard.set(portText, forKey: "douy.port")
        client.connect(host: address, port: portNumber)
    }
}
