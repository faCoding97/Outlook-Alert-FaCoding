# Windows package build

The shipped MSIX uses Windows package identity `Faraz.OutlookAlert`, app ID `App`, publisher `CN=Faraz Outlook Alert`, and the `userNotificationListener` capability. Startup defaults to enabled in the desktop manifest and is registered on first app launch independently of monitoring. The app checks the native state and respects Windows user and organization restrictions.

`build_package.py --publish ../Publish --output ../Payload/OutlookAlert.msix` copies the self-contained Windows publish output into an isolated staging directory, adds this manifest and assets, packs with Microsoft's MSIX SDK, signs with `osslsigncode`, verifies the Authenticode signature, and validates the package by unpacking it through the Microsoft SDK. The output `.metadata.json` pins package and public certificate hashes for Setup.

Build dependencies used in this workspace:

- Microsoft MSIX SDK source: https://github.com/microsoft/msix-packaging (Linux build with MSIX_PACK=ON, USE_VALIDATION_PARSER=ON, SKIP_BUNDLES=ON, tests/samples off).
- osslsigncode source: https://github.com/mtrojnar/osslsigncode (APPX support; built with OpenSSL3 and zlib).
- CMake4, GCC14, Python3, Pillow for exact-size package icon assets.

The developer private key and signing certificate PEM are intentionally excluded from deliverables. Rebuilding a distributable signed package requires a signing key under the developer's control whose certificate subject exactly matches the manifest Publisher. A self-signed development certificate is not a publicly trusted code-signing certificate. The provided Setup explicitly asks before trusting its pinned public leaf certificate in LocalMachine TrustedPeople; it does not add a root CA.

Certificate lifetime for this build is one year. Package signature validation and compilation on Linux do not establish successful Windows deployment, notification consent, notification delivery, or monitor placement. Those require a Windows integration run.
