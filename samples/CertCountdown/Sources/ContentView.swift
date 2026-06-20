import SwiftUI

struct ContentView: View {
    private let profile = ProvisioningProfile.loadEmbedded()
    @State private var now = Date()
    private let tick = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        ZStack {
            background.ignoresSafeArea()

            VStack(spacing: 28) {
                Spacer()

                VStack(spacing: 6) {
                    Image(systemName: "lock.shield")
                        .font(.system(size: 44, weight: .semibold))
                    Text("Sideload cert expires in")
                        .font(.headline)
                        .opacity(0.85)
                }

                if let remaining = remaining {
                    countdown(remaining)
                } else {
                    noProfile
                }

                Spacer()

                footer
            }
            .padding(.horizontal, 24)
            .foregroundStyle(.white)
        }
        .onReceive(tick) { now = $0 }
    }

    // MARK: - Countdown

    private var remaining: DateComponents? {
        guard let expiry = profile.expirationDate else { return nil }
        guard expiry > now else { return DateComponents(day: 0, hour: 0, minute: 0, second: 0) }
        return Calendar.current.dateComponents([.day, .hour, .minute, .second], from: now, to: expiry)
    }

    private func countdown(_ c: DateComponents) -> some View {
        HStack(alignment: .top, spacing: 14) {
            unit(c.day ?? 0, "DAYS")
            unit(c.hour ?? 0, "HRS")
            unit(c.minute ?? 0, "MIN")
            unit(c.second ?? 0, "SEC")
        }
    }

    private func unit(_ value: Int, _ label: String) -> some View {
        VStack(spacing: 4) {
            Text(String(format: "%02d", value))
                .font(.system(size: 46, weight: .bold, design: .rounded))
                .monospacedDigit()
            Text(label)
                .font(.caption2.weight(.semibold))
                .opacity(0.7)
        }
        .frame(minWidth: 64)
    }

    private var noProfile: some View {
        VStack(spacing: 8) {
            Text("No embedded profile")
                .font(.title3.weight(.semibold))
            Text("Running unsigned (Simulator). Sideload to a device to see the real 7-day countdown.")
                .font(.footnote)
                .multilineTextAlignment(.center)
                .opacity(0.75)
        }
    }

    // MARK: - Footer + theming

    private var footer: some View {
        VStack(spacing: 4) {
            if let name = profile.name {
                Text(name).font(.footnote.weight(.medium))
            }
            if let expiry = profile.expirationDate {
                Text("Expires \(expiry.formatted(date: .abbreviated, time: .shortened))")
                    .font(.caption2)
                    .opacity(0.7)
            }
            if let team = profile.teamName {
                Text(team).font(.caption2).opacity(0.55)
            }
        }
        .padding(.bottom, 12)
    }

    /// Green when comfortable, amber under 3 days, red under 1 day.
    private var background: LinearGradient {
        let hours = remainingHours
        let top: Color
        let bottom: Color
        switch hours {
        case let h where h == nil:           top = .indigo;       bottom = .black
        case let h? where h < 24:            top = .red;          bottom = .black
        case let h? where h < 72:            top = .orange;       bottom = .black
        default:                             top = .green;        bottom = .black
        }
        return LinearGradient(colors: [top.opacity(0.85), bottom],
                              startPoint: .top, endPoint: .bottom)
    }

    private var remainingHours: Double? {
        guard let expiry = profile.expirationDate else { return nil }
        return max(0, expiry.timeIntervalSince(now) / 3600)
    }
}

#Preview {
    ContentView()
}
