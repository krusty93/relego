---
title: Settings
description: Every stored Relego setting, its default, and the command that changes it.
eyebrow: Reference
sidebar:
  order: 2
---

These live in the server's database and persist across restarts. Change them
with the CLI, not by editing the database.

| Setting | Default | Accepted values | Change it with |
| --- | --- | --- | --- |
| Recap frequency | Daily | `daily`, `weekly` | `relego config schedule daily\|weekly [HH:MM]` |
| Recap time | `18:00`, server local time | 24-hour `HH:MM` | `relego config schedule daily 07:30` |
| Highlights per recap | `5` | `1`–`15` | `relego config count 8` |
| Send-to-Kindle address | Unset | Any `@kindle.com` address | `relego config email kindle "you@kindle.com"` |
| Inbox address | Unset | Any email address, or `""` to clear | `relego config email inbox "you@example.com"` |
| Highlight weight | Unweighted | `1`–`5` per highlight | `relego weight set <id> 5` |
| Exclusions | None | Per highlight, book, or author | `relego exclude add book 17` |

Print everything currently set:

```sh
relego config show
```

## Which channel do I need?

| You read on | Set | Result |
| --- | --- | --- |
| Kindle | Kindle address | EPUB delivered to the device |
| Kobo | Inbox address | HTML recap in your mailbox |
| Both | Both | Both, from the same recap |

Leave the inbox address unset if you only want Kindle delivery.
