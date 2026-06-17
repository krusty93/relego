# Security Policy

## Supported Versions

Relego uses independent semantic versioning for each component. Security fixes are applied to the **latest released version** of each component. Older versions are not actively patched.

| Component | Latest Version | Supported |
| --------- | -------------- | --------- |
| `server`  | 0.16.x         | ✅        |
| `cli`     | 0.13.x         | ✅        |
| `core`    | 0.7.x          | ✅        |

Versions below the latest minor release for each component are **not supported**.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

To report a vulnerability, use GitHub's private **[Security Advisories](https://github.com/krusty93/relego/security/advisories/new)** feature:

1. Navigate to the [Security tab](https://github.com/krusty93/relego/security) of this repository.
2. Click **Report a vulnerability**.
3. Fill in the advisory form with as much detail as possible, including:
   - A description of the vulnerability and its potential impact
   - Steps to reproduce or a proof-of-concept
   - Affected component(s) (`server`, `cli`, `core`) and version(s)
   - Any suggested mitigations if known

You can expect an initial response within **5 business days**. If the vulnerability is confirmed, a fix will be prioritised and a coordinated disclosure will be arranged with you before any public announcement.

Thank you for helping keep Relego and its users safe.
