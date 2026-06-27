# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.8.x   | :white_check_mark: |
| < 1.8   | :x:                |

## Reporting a Vulnerability

We take security vulnerabilities seriously. If you discover a security issue, please report it responsibly.

### How to Report

**Email:** Send details to [security@sandovaldavid.com](mailto:security@sandovaldavid.com)

**GitHub:** Use [GitHub Security Advisories](https://github.com/sandovaldavid/kioku/security/advisories/new)

Please include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

### What to Expect

- **Acknowledgment:** Within 48 hours
- **Initial assessment:** Within 1 week
- **Regular updates:** At least weekly until resolved
- **Public disclosure:** Coordinated after fix is available

### Security Features

Kioku implements several security measures:

1. **Path Traversal Protection:** All file operations validate paths stay within the vault
2. **Local-Only WebSocket:** Bridge binds to 127.0.0.1 only
3. **HTTPS Enforcement:** HTTP transport requires TLS in production
4. **API Key Authentication:** Bearer token authentication for HTTP endpoints
5. **Soft Delete:** Notes are moved to trash by default, not permanently deleted
6. **Dependency Scanning:** Automated vulnerability checks in CI

### Security Best Practices

When deploying Kioku:

1. **Use HTTPS** for HTTP transport (never expose plain HTTP)
2. **Set API keys** for all HTTP endpoints
3. **Keep dependencies updated** (run `dotnet list package --vulnerable` and `pnpm audit` regularly)
4. **Monitor logs** for suspicious activity
5. **Backup your vault** regularly (use git or other version control)
6. **Review permissions** - the server runs with your user's file access rights

### Known Limitations

- The server has the same file access permissions as the user running it
- WebSocket bridge is localhost-only (by design)
- No built-in rate limiting (use reverse proxy like nginx)
- API keys are stored in environment variables (ensure proper OS-level protection)

## Security Updates

Security updates are released as patch versions (e.g., 1.8.1, 1.8.2). Subscribe to releases to stay informed.

## Responsible Disclosure

We appreciate responsible disclosure and will acknowledge contributors (unless they prefer anonymity).
