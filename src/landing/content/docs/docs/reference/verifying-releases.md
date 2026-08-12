---
title: Verifying releases
description: Confirm the Relego images you run were built by this repository.
eyebrow: Reference
sidebar:
  order: 5
---

Relego's container images are published with build provenance attestations. You
can confirm an image came from this repository's CI before you run it.

You need the [GitHub CLI](https://cli.github.com/).

## Server image

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.server:latest \
  --owner Krusty93
```

## CLI image

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.cli:latest \
  --owner Krusty93
```

A successful run reports the workflow and commit the image was built from. A
failure means the image was not built by this repository — do not run it.

Pin a released tag instead of `latest` if you want a reproducible check.
