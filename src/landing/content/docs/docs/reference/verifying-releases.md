---
title: Verifying releases
description: Confirm the Relego images you run were built by this repository.
eyebrow: Reference
sidebar:
  order: 5
---

Relego's container images are published with build provenance attestations, and
every released tag carries a GitHub release attestation. You can confirm an image
came from this repository's CI, and that a release and its assets are the ones
that were published.

You need the [GitHub CLI](https://cli.github.com/).

## Server Docker image

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.server:latest \
  --owner krusty93
```

## CLI Docker image

```sh
gh attestation verify \
  oci://ghcr.io/krusty93/relego.cli:latest \
  --owner krusty93
```

A successful run reports the workflow and commit the image was built from. A
failure means the image was not built by this repository, do not run it.

Pin a released tag instead of `latest` if you want a reproducible check.

## Release attestation

Releases are immutable once release immutability is enabled for the repository,
and publishing one generates a release attestation. Verify a release and every
asset it contains with:

```sh
gh release verify <component>/v<version> --repo krusty93/relego
```

For example, `gh release verify server/v0.21.0 --repo krusty93/relego` once a
release with that tag exists.
