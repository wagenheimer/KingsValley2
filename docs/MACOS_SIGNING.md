# macOS release signing

## Signing and notarization (CI secrets)

`tools/macos/package_app.sh` always removes the quarantine attribute and deep-signs the
`.app`. Without a Developer ID it signs ad-hoc, which is enough to run locally but still
shows a Gatekeeper warning on a fresh download. To publish a notarized build, configure
these GitHub repository secrets:

| Secret | Purpose |
| --- | --- |
| `MACOS_SIGN_IDENTITY` | `Developer ID Application: Name (TEAMID)` |
| `MACOS_CERT_P12` | Base64 of the `.p12` certificate + private key |
| `MACOS_CERT_PASSWORD` | Password of the `.p12` file |
| `MACOS_KEYCHAIN_PASSWORD` | Password for the temporary runner keychain |
| `APPLE_API_KEY` | Base64 of the App Store Connect `.p8` API key |
| `APPLE_API_KEY_ID` | Key ID of the `.p8` key |
| `APPLE_API_ISSUER` | Issuer ID of the App Store Connect API |

Alternatively, use `APPLE_ID`, `APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` (app-specific
password) instead of the API key trio. Add them in **Settings → Secrets and variables →
Actions → New repository secret**. The values are encrypted, masked in workflow logs and
are never included in the repository or in the downloaded archives; they are also not
available to workflows triggered by pull requests from forks. A paid Apple Developer
Program membership and a `Developer ID Application` certificate are required.

All secrets are optional: forks and local builds still produce a valid ad-hoc signed
bundle. The release job verifies the result with `codesign --verify --deep --strict` and,
when signed with a Developer ID, `spctl`. Signing with a Developer ID uses
`tools/macos/Entitlements.plist` (`com.apple.security.cs.allow-jit` and
`com.apple.security.cs.allow-unsigned-executable-memory`), which is required for a
self-contained .NET app with the hardened runtime. Once the secrets are configured, a new
release build is notarized and staple-attached, so macOS Gatekeeper accepts it without
the "unknown developer" warning.
