# Security Policy

## Supported versions

PrivacyGuard 1.x receives security fixes. Older pre-release builds are not supported.

## Reporting a vulnerability

Please **do not open a public issue** for a vulnerability that could weaken the firewall, service,
privacy launcher, or policy-restore behavior.

Use GitHub's **Security → Report a vulnerability** (private security advisory) for this repository.
Include:

- affected version and Windows build;
- reproduction steps and expected/actual behavior;
- whether the issue requires administrator rights or a local user account;
- any proof-of-concept code, with secrets removed.

You should receive an acknowledgement within 7 days. Please allow time for a fix before public
disclosure.

## Scope and known limitations

PrivacyGuard is not a kernel sandbox or an antivirus. The following are **documented design
boundaries**, not vulnerabilities:

- a process running as the same user can read local time zone, UI language and IP address without
  using the app;
- Windows Firewall rules generally do not filter loopback / same-machine traffic;
- `TZ` / `LANG` / `--lang` are cooperative spoofing mechanisms, not mandatory OS restrictions;
- a local administrator can stop the service, replace binaries, or change configuration;
- clipboard pattern matching and the 1.5-second process scan are best-effort.

See the "安全模型与边界" section in [README.md](README.md) for details.
