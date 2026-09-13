# Code signing policy

Rift Revival intends to use free code signing provided by SignPath.io, with a certificate provided by SignPath Foundation, after the project is accepted into that programme.

Until acceptance, release notes must state that artifacts are unsigned. The project will not claim that a checksum is a publisher signature or ask users to disable Windows security.

## Roles

- Committer and reviewer: repository owner and approved maintainers listed by GitHub.
- Approver: repository owner.

No person may approve a release containing their unreviewed change. If the project has only one active maintainer, the build and signing request must still be derived from a reviewed repository commit and pass all automated checks.

## Release rules

- Build release artifacts from the public source repository through the documented build workflow.
- Include only project-owned source and redistributable third-party dependencies with their licence notices.
- Never sign or distribute Interstellar Rift executables, assets, saves, credentials, or private server configuration.
- Pin and verify downloaded build dependencies.
- Require manual approval for every signing request.
- Publish the source revision, artifact digest, validation result, and signing status with every release.
- Revoke or withdraw a release if its provenance or integrity cannot be established.
