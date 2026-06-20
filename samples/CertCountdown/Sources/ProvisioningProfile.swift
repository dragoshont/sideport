import Foundation

/// Reads the app's own `embedded.mobileprovision` to discover when the
/// signing cert / provisioning profile expires. On a free Apple ID sideload
/// this window is ~7 days — exactly the dead-man's-switch the homelab cares
/// about. In the Simulator there is no embedded profile, so everything is nil.
struct ProvisioningProfile {
    let expirationDate: Date?
    let name: String?
    let teamName: String?

    var isLoaded: Bool { expirationDate != nil }

    static func loadEmbedded() -> ProvisioningProfile {
        let empty = ProvisioningProfile(expirationDate: nil, name: nil, teamName: nil)

        guard let url = Bundle.main.url(forResource: "embedded", withExtension: "mobileprovision"),
              let data = try? Data(contentsOf: url) else {
            return empty
        }

        // The file is CMS / PKCS#7 signed binary with an XML plist embedded in
        // the clear. Scan for the plist envelope using a byte-safe encoding.
        guard let raw = String(data: data, encoding: .isoLatin1),
              let start = raw.range(of: "<?xml"),
              let end = raw.range(of: "</plist>") else {
            return empty
        }

        let plistText = String(raw[start.lowerBound..<end.upperBound])
        guard let plistData = plistText.data(using: .isoLatin1),
              let plist = try? PropertyListSerialization.propertyList(
                  from: plistData, options: [], format: nil) as? [String: Any] else {
            return empty
        }

        return ProvisioningProfile(
            expirationDate: plist["ExpirationDate"] as? Date,
            name: plist["Name"] as? String,
            teamName: plist["TeamName"] as? String
        )
    }
}
