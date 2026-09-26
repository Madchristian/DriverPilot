# Authenticated application updates

## Trust boundary and first migration

The app verifies packages before displaying its approval dialog. It launches only
`Setup.ps1` from the existing protected `Program Files/DriverPilot` installation,
never a script, assembly, verifier, certificate policy or executable from the
incoming package. The same `PackageTrust.cs` implementation is compiled into the
app and loaded by the **installed** Windows PowerShell 5.1 updater. Its certificate
pin is a source constant. There is no environment variable, command-line option,
configuration file, OS root-store entry or package field that changes that pin.
The installed public certificate is checked against the pin before use; an
incoming certificate cannot authorize itself.

**Existing releases through 0.3.2 have a hash-only updater. They cannot securely
bootstrap this feature. Do not use that old app-update path for migration.**
Obtain the new signed installer, independently compare the signer's certificate
fingerprint with the publisher, and verify the installer's Authenticode signature
before approving elevation (see ZERTIFIKAT-ANLEITUNG.txt). That independently
trusted installer carries the bootstrap script/verifier. Running a downloaded
Setup.ps1 directly is not a secure bootstrap procedure. Portable applications
remain usable for diagnostics but cannot elevate their local scripts for setup or
updates. Installed task reconfiguration and disabling still require explicit UAC.

Trust assumes Windows, the local administrator, the installed updater and the
publisher's private key are uncompromised. This is not protection against a local
administrator who can replace Program Files or alter the clock. Publisher signing
credentials must not be given to untrusted PR builds.

## Wire format and checks

* `package-manifest.json`: exact UTF-8 bytes, without BOM. A deliberately narrow
  JSON profile permits objects, ASCII strings without JSON escapes, and standard
  JSON whitespace only. Exactly `Product`, `Version`, `Files`; no null, arrays,
  comments, trailing content, unknown fields or duplicate/case-aliased keys.
* Product is `DriverPilot`. Version is canonical `Major.Minor.Patch`, exactly
  matching DLL FileVersion `Major.Minor.Patch.0`; updates must be strictly newer.
* Files is a complete flat filename-to-SHA256 map, including the future updater,
  verifier source, public certificate, application/runtime binaries and shipped
  documentation. It excludes only the manifest and its detached signature.
  Names are restricted to ASCII letters/digits/underscore/hyphen/dots and the
  supported extensions. Traversal, rooted names, ADS, device names, trailing
  spaces/dots, duplicate case aliases, subdirectories and unlisted entries fail.
  Reparse points in files, metadata, package root and all ancestors fail.
* `package-manifest.sig`: raw RSA PKCS#1 v1.5 signature with SHA-256 over the
  **exact manifest bytes** (not reserialized JSON, not the textual digest).
* SHA-256 pin of `certificates/DriverPilot.cer` DER:
  `2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B`.
  Certificate thumbprint for human comparison:
  `8C1B45FCCC089BF2A4393EEF38827B5D0BBF9775`.

A missing, invalid, foreign-key or expired signature fails closed, even if all
payload hashes have been recomputed. Merely importing a different trusted root
or shipping a different .cer cannot make a package valid. Authenticode on EXE,
DLL and Setup remains additional Windows publisher protection; the detached
signature authenticates the **entire** inventory, not only those three files.

## Time, expiration, revocation and rotation policy

Detached manifest signatures have **no trusted timestamp**. The pinned
certificate must be within its NotBefore/NotAfter interval at **every** source
and staged verification, using current UTC. A previously signed package stops
being installable when the certificate expires. An Authenticode timestamp does
not extend manifest-signature validity. There is no offline lifetime extension,
claimed signing-time field, grace period or expiry bypass.

No online CRL/OCSP check or emergency remote revocation list is implemented.
Pinning is authorization, not an assertion of public CA trust or online
revocation status. A key compromise requires stopping distribution and replacing
the updater through an independently verified bootstrap channel. For planned
rotation, ship a reviewed updater with the replacement pin via an update signed
by the still-authorized, still-valid old key, or independently verify a new
installer. Replacing a package's certificate alone never rotates trust. If the
old key is expired/unavailable, use the independently verified installer. The
single-pin design intentionally does not silently trust arbitrary successor keys.

## Approval, staging and replacement

1. The GUI checks installed location, ownership, write ACLs and reparses; verifies
   the candidate; displays version/source and asks for approval.
2. SHA-256 of those verified manifest bytes travels as `ApprovedManifestHash`
   through the UAC invocation. The installed elevated updater requires this hash
   and re-verifies the source signature, version and inventory. A different
   *validly signed* manifest swapped during UAC is rejected too.
3. Only after successful source authentication may it create a uniquely named,
   administrator/SYSTEM-owned staging directory under protected ProgramData.
   Existing untrusted owners or writable ACLs are rejected, not repaired. New
   directories receive protected ACLs at creation. Source files are copied as
   data; the exact already-authenticated manifest/signature bytes are preserved.
4. It verifies the protected copy again, including signature, hashes, inventory,
   version and approval hash. It prepares and verifies a protected sibling under
   Program Files on the destination volume. No incoming code executes.
5. After waiting for the original process and refusing running instances, it
   renames the old installation to `DriverPilot.previous-<guid>` and moves the
   complete verified sibling into place. It never overlays obsolete DLLs/scripts.
   A failed second rename restores the previous directory. Backups are retained
   for manual recovery; a power loss between renames may require manual recovery.
   This is not a transactional OS installer and task/shortcut registration after
   replacement is not part of the directory rollback transaction.

No package verification depends on a candidate's Setup.ps1, PackageTrust.cs or
certificate-store modifications. Source mutations during copying cannot bypass
verification of the protected copy. Driver selection, maintenance windows,
backups, explicit user approval/UAC and no-reboot behavior are unchanged.

## Builds and safe local tests

`Build.ps1 -Sign` first Authenticode-signs the existing artifacts, then hashes the
complete clean published output, signs the exact manifest, and verifies it.
`Build-Release.ps1 -Sign` verifies and packages those exact files plus metadata.
`Build.ps1` without `-Sign` deletes stale output/signatures and produces an
explicitly unsigned **development-only** build. `Build-Release.ps1` refuses
unsigned packaging before running a compiler. PR/branch CI runs adversarial
fixture tests and app tests, but does not publish an unsigned installer or ZIP.

Run on Windows with .NET SDK 10 and Windows PowerShell 5.1:

```powershell
./tests/Test-PackageSecurity.ps1 -Dotnet path/to/dotnet.exe
./Build.ps1 -Dotnet path/to/dotnet.exe
./Test-Pipeline.ps1
```

The security suite generates ephemeral in-memory RSA keys/certificates, exercises
the linked production C# core and the same source under Windows PowerShell,
checks the public production pin refuses fixture keys, and tests staged mutation,
approval binding, JSON/path/version attacks, expiry and ACL policies. Reflection
for fixture certificates exists only in tests. No production verifier bypass,
private release key, certificate-store import, real installation, UAC or driver
installation is needed. Set `TMPDIR` to a scratch directory if desired.

Application self-tests run on a scratch copy to avoid adding diagnostics to the
strict release inventory. Actual signed installer execution remains restricted
to explicitly authorized signing runs on disposable CI machines; local security
tests do not claim to exercise Windows elevation or a live installation.
