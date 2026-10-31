# Notebook fixtures

Committed shape of the Kindle notebook profile, one directory per allowlisted region
(`tests/fixtures/<regionId>/`). A fixture change requires a profile version bump in
`src/profile.ts`; see
[`contracts/extension-notebook-profile.md`](../../../../specs/011-kindle-background-sync/contracts/extension-notebook-profile.md).

Every region carries the full set — library list, book page, truncation warning, sign-in redirect,
changed layout, empty — so a region whose markup diverges can be updated on its own.

The notebook markup Amazon serves is region-neutral today, so `sign-in.html` is the only file that
differs between regions: its `data-signin-host` holds the region's sign-in host, which
`isSignInPage` matches against the region's `signInHostPatterns`. No fixture is translated copy; the
parser reads structure and attributes only, never text.
