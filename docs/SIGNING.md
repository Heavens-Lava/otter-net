# Signing and notarization plan

Status (2026-10-21): **a plan only.** Nothing has been bought, signed up
for, or signed. Every package built so far, including the internal preview
(`docs/PREVIEW_RELEASE.md`), is unsigned. The prices below were checked on
2026-10-21 (sources linked; confirm them again before buying). The
decisions at the end are the owner's.

## Why it matters

- **Windows.** Symantec quarantined the first unsigned otter-net `otter.exe`
  (`Heur.AdvML.D`, 2026-10-15; otterPS `docs/OTTER_WINDOWS_INSTALLER.md`).
  SmartScreen warns about unsigned, unknown downloads ("Windows protected
  your PC"). Smart App Control on Windows 11 can block unsigned programs
  outright.
- **macOS.** An unsigned, unnotarized `.pkg` is blocked when it is opened
  from Finder. Since macOS 15 (Sequoia) Control-click > Open no longer
  overrides this: users must go to System Settings > Privacy & Security
  ([Apple, 2024-08-06](https://developer.apple.com/news/?id=saqachfa)). A
  downloaded `otter` from the tarball is quarantined the same way.
- **Linux.** Individual `.deb` files are normally not signed; trust comes
  from a signed apt repository, if there is one.

## What must be signed

| Platform | Item | How | Notes |
|---|---|---|---|
| Windows | `otter.exe` (Native AOT) | Authenticode, SHA-256, RFC 3161 timestamp | both x64 and Arm64 |
| Windows | `otter-desktop.exe` (self-contained single file) | Authenticode | the `*_cor3.dll` beside it are Microsoft's and already Microsoft-signed; check with `signtool verify /pa` and leave them alone |
| Windows | the `.msi` | Authenticode (`signtool` signs MSI files) | sign the executables **before** the MSI is built (they are inside its cabinet), then the MSI |
| Windows | the `.zip` | not signable; its executables are signed | publish SHA-256 sums beside it (already done) |
| macOS | `otter` (Native AOT, arm64 and x64) | `codesign` with a **Developer ID Application** certificate, `--options runtime` (Hardened Runtime) and `--timestamp` | Native AOT needs no entitlements ([Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)) |
| macOS | the `.pkg` | `productbuild --sign` (or `productsign`) with a **Developer ID Installer** certificate | then notarize and staple |
| macOS | the `.tar.gz` | not signable; its `otter` is signed and notarized | a bare executable cannot be stapled: Gatekeeper checks its notarization online the first time ([Apple](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow)) |
| Linux | the `.deb` | normally unsigned (`dpkg-sig` is gone from Debian; `debsigs` is not checked by apt) | only an apt repository is signed: its `InRelease`/`Release.gpg`, with an OpenPGP key users install as a `Signed-By:` keyring |
| all | `SHA256SUMS` | optionally a detached OpenPGP or Sigstore signature | free |

One Windows certificate (or signing identity) and one pair of Apple
certificates cover every product: they name the publisher (Jeffrey Macy),
not otter-net. otterPS's MSI (if it is ever published) would use the same.

## Windows options

| Option | Cost (as of 2026-10-21) | Key storage | Fits an individual? | CI |
|---|---|---|---|---|
| **Azure Artifact Signing** (Microsoft; called "Trusted Signing" until January 2026) | **Basic US$9.99/month** (5,000 signatures; US$0.005 each beyond); Premium US$99.99/month (100,000). A paid Azure subscription (pay-as-you-go) is required; a month is billed in full from the day the account is made ([pricing](https://azure.microsoft.com/en-us/pricing/details/artifact-signing/), [Azure Retail Prices API](https://prices.azure.com/api/retail/prices?$filter=contains(productName,'Signing')), [FAQ](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)) | Microsoft's HSMs (FIPS 140-3 Level 3); no key to look after; certificates last 72 hours and are renewed daily, so every signature must be timestamped | **Yes, if the owner is in the US or Canada** ("Individual" billing account; identity check with a government ID and a face check through Microsoft Entra Verified ID; renewed periodically) ([quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)) | `azure/artifact-signing-action@v2` on a Windows x64 runner, OIDC login (`azure/login`), no stored secret ([action](https://github.com/Azure/artifact-signing-action)) |
| OV/IV certificate, cloud HSM: **SSL.com eSigner** | certificate from US$129/year (1 year) to US$96.75/year (5 years) **plus** eSigner US$20/month (20 signatures) or US$180/year (240), US$1 per extra signature ([price](https://www.ssl.com/certificates/code-signing/buy/), [eSigner](https://www.ssl.com/guide/esigner-pricing-for-code-signing/)) | SSL.com's cloud HSM | yes (IV certificate for individuals) | `sslcom/actions-codesigner`, needs a TOTP secret in GitHub |
| OV certificate on a USB token (Sectigo via resellers, Certum, SSL.com) | Sectigo ~US$319/year (token included); Certum €139-€209/year (card and reader set €169); SSL.com US$129/year + YubiKey US$379 ([SSL Dragon](https://www.ssldragon.com/product/sectigo-code-signing-ssl/), [Certum](https://shop.certum.eu/code-signing.html)) | the token must be plugged in to sign (FIPS 140-2 Level 2 hardware is required since June 2023) | yes | **no hosted CI**: signing happens on the owner's machine (or a self-hosted runner with the token) |
| EV certificate | US$349-US$972/year (SSL.com, DigiCert) | token or cloud HSM | normally organizations only | as above |
| SignPath Foundation (free) | free | SignPath's HSM | **no**: needs an OSI-approved open-source license ([terms](https://signpath.org/terms)); the Otter Free Use License 1.1 is source-available | - |
| Certum Open Source | €25-€69 | card or cloud | probably not: for open-source developers; source-available is a judgment call for Certum | - |

Every certificate issued since 2026-03-01 lasts at most **460 days**
(CA/Browser Forum ballot CSC-31, [2025-11-17](https://cabforum.org/2025/11/17/ballot-csc-31-maximum-validity-reduction)),
so a token or cloud certificate is reissued at least yearly.

**SmartScreen reputation** comes from the publisher identity and from each
file's download history. Since 2024 an EV certificate no longer gives
instant reputation ("Paying a premium for EV solely to avoid SmartScreen
warnings is no longer justified", [Microsoft](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)).
With any option, the first signed releases can still show a warning until
downloads build reputation (weeks, hundreds of clean installs); signing
removes the "Unknown publisher" text and lets reputation accumulate on one
identity across versions. Keep one identity: changing certificates or
providers resets the publisher signal. False positives (such as the
Symantec quarantine) are reported to the antivirus vendor and to Microsoft
Security Intelligence (free, <https://www.microsoft.com/en-us/wdsi/filesubmission>).

winget does not require a signed installer, but scans every submission
(several antivirus engines and a Defender dynamic scan); an unsigned MSI is
more likely to be flagged. The Microsoft Store signs MSIX packages itself
(free; individual developer registration has been free since 2025-09-10);
for an MSI/EXE listing the Store requires the installer **and every PE file
in it** signed with a certificate that chains to the Microsoft Trusted Root
Program - whether Artifact Signing's certificates qualify there is not
documented (Microsoft is moving them off CCADB while keeping them trusted by
Windows), so check before choosing that route.

### Recommendation (Windows): Azure Artifact Signing, Basic

- **Cost:** about US$120 a year (US$9.99 x 12), far below the signatures
  needed (a release signs about 6 files: 2 executables + 1 MSI per
  architecture; a CI preview run the same).
- **No key to protect, no hardware, signs from hosted GitHub Actions** with
  OIDC (no long-lived secret in the repository).
- **Eligibility is the catch:** an individual must be in the US or Canada.
  If the owner is not, the fallback is **SSL.com IV + eSigner** (about
  US$129 + US$180 a year, CI-capable with a TOTP secret).
- A token-based OV certificate is the cheapest per year only when signing
  by hand on the owner's machine, which the project avoids (no executables
  are built on the owner's Windows machine until they can be signed).

## macOS

- **Apple Developer Program, US$99/year** (individual enrolment):
  Developer ID Application and Developer ID Installer certificates, and
  notarization ([Apple](https://developer.apple.com/programs/whats-included/)).
- Notarization: `xcrun notarytool submit --wait` with an **App Store Connect
  API key** (key ID, issuer ID, `.p8` file; no Apple ID password in CI),
  then `xcrun stapler staple` on the `.pkg`. Requirements: Hardened Runtime,
  a secure timestamp, no `get-task-allow` ([Apple](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)).
  Submit the `.pkg` (it contains the signed `otter`) and, for the tarball,
  a zip of the signed `otter` (zips cannot be stapled; the ticket is looked
  up online).
- No alternative: only Apple issues Developer ID certificates.

## Linux

- Keep the `.deb` and `.tar.gz` unsigned files with published SHA-256 sums
  (as now), optionally with a detached signature of `SHA256SUMS`.
- An **apt repository** (so `apt upgrade` updates otter-net) is a separate
  decision: free to run (a GPG key, e.g. Ed25519 or RSA-3072 with SHA-256
  self-signatures - Debian 13's apt rejects SHA-1 bindings since 2026-02-01;
  repository files generated in CI with `reprepro` or `aptly` and served
  from GitHub Pages). Not needed for the preview.

## Pipeline changes (when the owner approves)

All in `package.yml`, all in the **build** jobs, before anything is packed:

1. **Windows build jobs** (`windows-build`):
   - After `dotnet publish` of `otter` and `otter-desktop`, and before
     `package.ps1` makes the zip and the MSI: sign `otter.exe` and
     `otter-desktop.exe`. `package.ps1` gets a `-SignCommand` hook (or is
     split: publish, sign, pack) so the signed files are what the zip and
     the MSI carry. Then sign the MSI.
   - Artifact Signing's action runs on Windows x64 runners only: the
     `win-arm64` job builds on `windows-11-arm` as now, uploads the unsigned
     executables, and a small `windows-sign` job on `windows-latest` signs
     both architectures' files (signing does not care about the file's
     architecture), then packs the arm64 zip/MSI there or sends the signed
     files back. Simplest: build both MSIs on `windows-latest` (WiX builds
     an arm64 MSI on x64) from signed executables.
   - Steps: `azure/login@v2` (`client-id`, `tenant-id`, `subscription-id`
     from repository **variables**; OIDC, `permissions: id-token: write`
     on that job only), then `azure/artifact-signing-action@v2` with
     `endpoint`, `signing-account-name`, `certificate-profile-name`,
     `files-folder`, `file-digest: SHA256`,
     `timestamp-rfc3161: http://timestamp.acs.microsoft.com`,
     `timestamp-digest: SHA256`.
   - Check: `signtool verify /pa /v` on every signed file; the test jobs
     also check `Get-AuthenticodeSignature` is `Valid` for the installed
     files.
2. **macOS build jobs** (`macos-build`):
   - Import the Developer ID certificates (`.p12` + password, base64 in
     secrets) into a temporary keychain made for the job (`security
     create-keychain`, `security import`, `security set-key-partition-list`),
     deleted at the end.
   - `codesign --force --options runtime --timestamp --sign "Developer ID
     Application: Jeffrey Macy (TEAMID)" otter` before packing; `productbuild
     --sign "Developer ID Installer: ..."` in `package.ps1` (a `-SignIdentity`
     parameter); `notarytool submit --key ... --key-id ... --issuer ...
     --wait`; `stapler staple` the `.pkg`.
   - Test jobs add `spctl --assess --type install` (pkg) and `codesign
     --verify --strict` / `spctl --assess --type execute` (otter).
3. **Linux:** no change (optionally `gpg --detach-sign SHA256SUMS`).
4. **Only on approved runs:** signing jobs run only on `workflow_dispatch`
   from a protected branch or tag, in a GitHub **environment** (e.g.
   `signing`) with required reviewer = the owner, so a pull request can
   never reach the signing identity. Pull requests keep building unsigned.

## Secrets handling

- **Azure:** no secret at all with OIDC: a Microsoft Entra app registration
  with a federated credential for `repo:Heavens-Lava/otter-net:environment:signing`,
  given only the "Artifact Signing Certificate Profile Signer" role on the
  one certificate profile. IDs go in environment variables, not secrets.
- **Apple:** in the `signing` environment's secrets: the two `.p12` files
  (base64) and their password, the App Store Connect API key (`.p8`,
  base64), key ID and issuer ID; a role-limited API key ("Developer"), not
  an admin one. The Apple ID password is never stored.
- **SSL.com eSigner (fallback):** username, password, credential ID and TOTP
  secret in the `signing` environment's secrets; anyone with them can sign
  as the owner, so the environment's reviewer rule matters most here.
- Nothing is echoed to logs; temporary keychains and files are deleted in
  `always()` steps; secrets are never available to `pull_request` runs.

## What the owner must decide or approve

1. **Spend:** Azure Artifact Signing Basic (~US$120/year) and the Apple
   Developer Program (US$99/year) - about **US$220/year** in total. (Or the
   SSL.com fallback, ~US$310/year, if Artifact Signing's individual
   eligibility does not apply.)
2. **Eligibility and identity:** is the owner an individual in the US or
   Canada (Artifact Signing), and is the published publisher name
   "Jeffrey Macy" (the certificate shows the validated legal name and city,
   state, country)? Or sign as an organization?
3. **Approve the signing environment** (`signing`, owner as required
   reviewer) and which runs may sign (tags / manual runs only).
4. **Whether otterPS's MSI** (unpublished) is signed with the same identity,
   and whether otter-net's identity replaces otterPS's or sits beside it
   (`docs/PREVIEW_RELEASE.md`) - signing does not depend on it, but
   SmartScreen reputation accumulates on whatever is published first.
5. **Linux:** stay with signed checksums, or run an apt repository (key
   custody decision).
6. **Store route:** if the Microsoft Store is wanted for otter-net, MSIX
   (Store-signed, free) avoids the certificate question for that channel.
