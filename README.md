![Relego landing page hero section in dark theme](docs/assets/landing-hero-dark.jpg)

<h1 align="center">Relego</h1>

<p align="center">
  Revisit your highlights, delivered to your Kindle. For free.
</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Release](https://github.com/Krusty93/relego/actions/workflows/release.yaml/badge.svg)](https://github.com/Krusty93/relego/actions/workflows/release.yaml)
[![GitHub Release](https://img.shields.io/github/v/release/krusty93/relego)](https://github.com/krusty93/relego/releases)

## Why Relego

- **E-ink first**: recaps delivered as native Kindle documents, not push notifications on your phone
- **Free and self-hosted**: no subscription, no data leaving your infrastructure
- **No lock-in**: your highlights stay yours, in an open format
- **Privacy**: your reading habits are not sent to any cloud service

---

## How it works

1. Connect your Kindle via USB and run `relego import` — highlights are imported from `My Clippings.txt`
2. The server selects a daily or weekly subset of highlights using spaced repetition (weighted by your preferences)
3. A recap document is sent to your Kindle email address via Amazon's Send-to-Kindle service
4. Open the recap on your Kindle like any other book

### What a recap looks like

Each recap is an EPUB document delivered to your Kindle. Here's an example of what you'll see when you open it:

> #### Relego Daily Recap (2026-05-21 18:00)
>
> - _"Care About Your Craft"_
>   — **The Pragmatic Programmer** by David Thomas & Andrew Hunt
>
> - _"Clean code is simple and direct."_
>   — **Clean Code** by Robert C. Martin
>
> - _"In a hole in the ground there lived a hobbit."_
>   — **The Hobbit** by J.R.R. Tolkien
>
> - _"The only way to do great work is to love what you do."_
>   — **Steve Jobs** by Walter Isaacson
>
> - _"Violence is the last refuge of the incompetent."_
>   — **Foundation** by Isaac Asimov

Each highlight includes the quote, the book title, and the author, making it easy to recall context at a glance. The number of highlights per recap is configurable (default is 5).

## Getting started

### 1. Connect your Kindle device

Connect your Kindle device to your computer via USB cable.

### 2. Run the server

```sh
docker network create relego

docker run -d \
  --name relego-server \
  --restart unless-stopped \
  -e KINDLE_EMAIL=your-address@kindle.com \
  -e SMTP_HOST=smtp.example.com \
  -e SMTP_PORT=587 \
  -e SMTP_USER=user@example.com \
  -e SMTP_PASSWORD=yourpassword \
  -p 8080:8080 \
  -v relego-data:/data \
  --network relego \
  ghcr.io/krusty93/relego.server:latest
```

Replace the `SMTP_*` values with those for your provider.

> Amazon Send-to-Kindle only accepts emails from approved senders. Add the email address you are going to use in your Amazon "Approved Personal Document E-mail List" before testing delivery.
>
> Gmail and Outlook personal accounts do not support SMTP with password authentication. Use instead a free SMTP relay like [Resend](https://resend.com/docs/send-with-smtp), [MailerSend](https://www.mailersend.com/help/smtp-relay) or [Mailgun](https://www.mailgun.com/features/smtp-server/) instead. They offer a free tier with a generous limit of free emails. Otherwise, you can use your own SMTP relay server.

### 3. Import the Kindle highlights

Import Kindle highlights using the TUI. It automatically detects the path to your Kindle:

<details>
  <summary>Docker (no install)</summary>
  **Windows** (Kindle mounts as drive `D:`):

  Replace `D` with the actual Kindle drive letter on your machine.

  ```powershell
  docker run `
    -it `
    -v "D:\documents:/kindle:ro" `
    --network relego `
    -e SERVER_URL="http://relego-server:8080" `
    ghcr.io/krusty93/relego.cli:latest
  ```

  > NB: Follow the [WSL documentation](https://learn.microsoft.com/en-us/windows/wsl/connect-usb) to allow WSL to access the Kindle device. The simpler option is to copy `My Clippings.txt` to a local folder and run the image from there:
  >
  >```powershell
  >docker run `
  >  -it `
  >  -v "$(Get-Location):/kindle:ro" `
  >  --network relego `
  >  -e SERVER_URL="http://relego-server:8080" `
  >  ghcr.io/krusty93/relego.cli:latest
  >```

  **macOS** (Kindle mounts at `/Volumes/Kindle`):

  ```sh
  docker run \
    -it \
    -v "/Volumes/Kindle/documents:/kindle:ro" \
    --network relego \
    -e SERVER_URL="http://relego-server:8080" \
    ghcr.io/krusty93/relego.cli:latest
  ```

  **Linux** (Kindle mounts at `/media/$USER/Kindle`):

  ```sh
  docker run \
    -it \
    -v "/media/$USER/Kindle/documents:/kindle:ro" \
    --network relego \
    -e SERVER_URL="http://relego-server:8080" \
    ghcr.io/krusty93/relego.cli:latest
  ```

</details>

<details>
  <summary>Windows</summary>

#### winget

  ```powershell
  winget install Krusty93.Relego
  relego
  ```

#### Installer script

  ```powershell
  irm https://raw.githubusercontent.com/Krusty93/relego/main/install.ps1 | iex
  relego
  ```

The installer detects your Windows architecture automatically and prints the path where `relego.exe` was saved.

</details>

<details>
  <summary>macOS / Linux</summary>

  ```sh
  curl -fsSL https://raw.githubusercontent.com/Krusty93/relego/main/install.sh | sh
  ~/.local/bin/relego
  ```

The installer detects your OS and architecture automatically and prints the path where `relego` was saved. If `~/.local/bin` is not already on your `PATH`, add `export PATH="$HOME/.local/bin:$PATH"` to your shell profile (`~/.zshrc`, `~/.bashrc`, or `~/.profile`), then open a new terminal.

</details>

The native client automatically connects to `http://localhost:8080`. If you ran the server on a different host machine or port, override `SERVER_URL` before starting the TUI:

```powershell
# PowerShell
$env:SERVER_URL = "http://192.168.1.10:8080"
relego
```

```sh
# macOS / Linux
export SERVER_URL=http://192.168.1.10:8080
relego
```

For Docker, keep the same commands shown above and replace `http://relego-server:8080` with your server URL.

### 4. Complete the first sync in the TUI

Once the TUI is open:

1. Press `S` to open Settings, set your Kindle email, and press `T` to send a test email.
2. Press `Esc` to go back to the books screen.
3. Press `I` to import highlights. Relego auto-detects `My Clippings.txt` when you used one of the standard paths above; otherwise enter the path manually and press `Enter`.

Prefer automation or scripting? The raw CLI is still available:

```sh
relego import <path>
```

That's it. After the first import completes, your first recap will arrive on the next scheduled delivery (default: every day at 18:00).

---

## Interactive mode

Run `relego` without arguments to open the interactive TUI:

Use the TUI to configure the server, browse highlights, and manage exclusions. For automation and scripting, use the CLI commands directly (see CLI reference).

Theme selection for TUI:

- `RELEGO_THEME=dark` (default)
- `RELEGO_THEME=light`

## CLI reference

|                   Command                        |              Description                  |
|--------------------------------------------------|-------------------------------------------|
| `relego`                                         | Open interactive TUI                      |
| `relego import [path]`                           | Import highlights from `My Clippings.txt` |
| `relego status`                                  | Show server status and next recap         |
| `relego config schedule <daily\|weekly> [HH:MM]` | Set recap schedule                        |
| `relego config schedule show`                    | Show current schedule                     |
| `relego config count show`                       | Show current highlights-per-recap setting |
| `relego config count <1-15>`                     | Set highlights per recap (default: 5)     |
| `relego config kindle-email <address>`           | Set the Kindle delivery email address     |
| `relego exclude highlight <id>`                  | Exclude a highlight from all recaps       |
| `relego exclude book <title>`                    | Exclude all highlights from a book        |
| `relego exclude author <name>`                   | Exclude all highlights from an author     |
| `relego exclude remove highlight <id>`           | Re-include a highlight                    |
| `relego exclude remove book <title>`             | Re-include a book                         |
| `relego exclude remove author <name>`            | Re-include an author                      |
| `relego exclude list`                            | List all exclusions                       |
| `relego weight set <id> <1-5>`                   | Set highlight weight                      |
| `relego weight list`                             | Show weighted highlights                  |
| `relego version`                                 | Print version                             |

---

## Supply chain verification

Verify Docker image origin via GitHub CLI:

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.server:latest \
  --owner Krusty93
```

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.cli:latest \
  --owner Krusty93
```

---

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. Useful documentation:

- [Product Requirements Document](docs/PRD.md)
- [Developer Experience Design](docs/DX.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Architecture Decision Records](docs/adr/)

## License

MIT, see [LICENSE](LICENSE).
