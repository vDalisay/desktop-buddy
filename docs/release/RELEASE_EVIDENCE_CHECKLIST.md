# Private release-evidence checklist

Keep secrets, certificates, Steam credentials/config, signing keys, binaries, and retained candidates outside public repository artifacts. The tracked record may contain only non-secret identities and hashes.

- [ ] Distribution, version, final Git SHA, workflow/run ID, and Steam/itch Build ID recorded.
- [ ] Runtime AppID and Workshop-owner AppID recorded from the exact candidate.
- [ ] Pinned Godot/editor/template/addon/SteamCMD-or-Butler identities recorded.
- [ ] Complete payload manifest and its SHA-256 recorded; tested bytes match promoted bytes.
- [ ] Authenticode verification records valid chain, approved subject/thumbprint, and timestamp for every shipped PE.
- [ ] Source/project/debug/map/development-AppID files absent; expected native runtimes present.
- [ ] Required hosted CI and exact-candidate Windows, persistence, Paint, Studio, Work, Steam/Workshop/Cloud, DPI, performance, and soak results linked.
- [ ] Hardware/account/configuration gaps listed as pending, never passed.
- [ ] Owner approver, allowed branch, environment protection, and public-source policy decisions recorded.
- [ ] Upload/promotion/release approval and post-release sanity result recorded separately.
