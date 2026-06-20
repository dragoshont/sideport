import SwiftUI

/// Dead-simple dice roller — tap to roll a fair six-sided die.
/// One of the "random simple apps" used to exercise the homelab's multi-app
/// AltServer-Linux signing pipeline.
struct ContentView: View {
    @State private var value = 1
    @State private var rolling = false

    var body: some View {
        ZStack {
            LinearGradient(colors: [.purple.opacity(0.85), .black],
                           startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()

            VStack(spacing: 36) {
                Text("DiceRoll")
                    .font(.largeTitle.weight(.bold))
                    .foregroundStyle(.white)

                Image(systemName: "die.face.\(value).fill")
                    .font(.system(size: 160))
                    .foregroundStyle(.white)
                    .rotationEffect(.degrees(rolling ? 360 : 0))
                    .animation(.easeOut(duration: 0.35), value: rolling)

                Button(action: roll) {
                    Text("Roll")
                        .font(.title2.weight(.semibold))
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 16)
                        .background(.white.opacity(0.15), in: Capsule())
                        .foregroundStyle(.white)
                }
                .padding(.horizontal, 48)
            }
        }
    }

    private func roll() {
        rolling.toggle()
        value = Int.random(in: 1...6)
    }
}

#Preview {
    ContentView()
}
