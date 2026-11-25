# ADR-010: Anonymous, Opt-Out Usage Telemetry from the Server Container

**Status:** Accepted
**Date:** 2026-11-23

## Context

Relego is self-hosted software. ADR-006 makes the server a Docker image that a user runs on hardware the project never sees, which means the project has no server-side aggregate of its own and today knows nothing about its install base: every install is a silent, unobservable copy of the code. Releases ship into that silence and no signal comes back.

Three product questions must become answerable, because each one drives a decision the project cannot currently make on evidence:

1. **How many installs are active in a month?** Adoption is the input to every prioritisation and sustainability judgement. Today it can only be inferred from stars, downloads and issues, none of which measure use.
2. **Which server versions are running?** Without this, the project cannot tell a release that everyone upgraded to from a release nobody adopted, and cannot judge how long an older version must keep working or when a fix reaches the people who need it. Since Relego is on 0.x semver, it also decides what a breaking change is allowed to do: a change that breaks an API can be made when the field has mostly moved on, and has to be deferred, versioned or shipped with a compatibility path when it has not — and today there is no way to tell which of those is true.
3. **Which countries do installs run in?** This sets where documentation, language support and support-response expectations should point, and it is invisible from the outside.

Two written commitments constrain any mechanism that answers them:

- `PRIVACY.md` currently states that the software "contains no analytics, telemetry, or tracking". That sentence is either true or removed; it cannot be quietly softened.
- NFR-02 in `docs/prds/prd-v1.md` requires that Relego adds no **mandatory** account, telemetry, or hidden transfer. Default-on collection would violate it if "not mandatory" were ever read as "opt-in only".

The project is small and open source. It cannot fund or operate an analytics database, and it should not introduce a second service to deploy, expose, patch and back up just to count installs. Any answer has to fit inside the container ADR-006 already defines and inside a budget of zero.

The question is therefore not whether to collect, but how little can be collected and still answer the three questions. A mechanism that fails any of the properties below is worse than no data, because it either costs the project more than it returns or costs the user something they did not agree to.

The mechanism must satisfy four properties:

1. An install must remain one install across restarts and upgrades, or the count measures process starts instead of installations.
2. The payload must be small enough to describe in a sentence, so the policy can enumerate it exhaustively rather than summarise it.
3. Failure must be silent, because a self-hosted user cannot be asked to debug the project's analytics.
4. The off switch must be documented, durable and verifiable, because that is what makes the collection non-mandatory in fact as well as in wording.

Alternatives rejected:

- **No telemetry at all.** It preserves the current privacy wording unchanged, but it leaves the project unable to tell adoption from silence and unable to see version drift or a release nobody upgraded to. The three questions stay unanswerable permanently, not just today.
- **Opt-in telemetry.** OSS opt-in telemetry samples a small, self-selected minority, so it cannot answer "how many installs run version X" — the population that opts in is not the population that exists. It trades away the only reason to collect the data in exchange for a consent flow that changes nothing else.
- **Self-hosted Umami, GoatCounter or Matomo.** Each needs a database and a publicly reachable service that the project would have to fund, deploy, patch and back up. That is exactly the operational surface a single-maintainer OSS project should not take on for aggregate counts.
- **Umami Cloud (Hobby).** The cheapest managed dashboard, but its "Visitors" metric is an IP + User-Agent + salt session hash with a monthly rotating salt, so it is not a true MAU unless a distinct identifier is supplied. Each stored event property is also billed as an extra event, and the free tier exposes no API.
- **Aptabase.** App-shaped, with the version and country breakdowns this decision needs, but by design it provides no monthly active users — the primary requirement.
- **TelemetryDeck.** Privacy-strong and EU-hosted, but signal-count oriented, with three months of retention on the free plan and no first-class MAU metric.
- **Cloudflare Worker + Analytics Engine / Counterscale.** Full ownership at effectively zero cost, and `request.cf.country` supplies country without a geo database, but it means writing and maintaining a second deployable service and its dashboard. More code and more operations than one HTTP POST, which is the whole requirement.
- **Google Analytics or Plausible Cloud.** GA is disqualified by the project's privacy stance; Plausible has no free tier.
- **Adding OS detection later by mounting the Docker socket or host paths.** Disproportionate privilege for a dimension that is nearly constant across container installs, and a capability the container should not hold.

This ADR builds on **ADR-006** (Docker-only server distribution).

## Decision

### 1. Scope: the server container only

Telemetry exists in the server container and nowhere else. The `relego` CLI, the web UI, the browser extension and the install scripts send nothing, and gain no identifier, no configuration switch and no dependency on this decision. The reason is not caution but relevance: telemetry exists so the project can size and steer **server releases**, and the server container is the unit that carries a version and runs in a country. A CLI invocation on a laptop has no release cadence to steer and no meaningful country to report.

Operating system is deliberately out of scope, and it is neither collected nor inferred. Inside the container the OS is always the runtime image's Linux — it is a property of the Dockerfile, not of the user — so a reported "OS" would be a constant dressed up as a dimension. Detecting the **host** OS would mean mounting host paths and reading a value the container is not entitled to, which is a privilege escalation for a field that would almost always repeat the same answer. The reported architecture already distinguishes the cases that actually differ (for example an ARM board against an x86 server).

**Rationale**: scoping to the server container keeps the promise narrow enough to be honest. The other components are either short-lived (the install scripts), confined to a browser (the extension) or pointed only at a server the user chose (the CLI and the web UI), so none of them can report a version-and-country pair that means anything. Restricting collection to one component also gives every reader one place to look when asking what Relego sends, instead of five component-specific answers that could silently diverge.

### 2. Approach: one HTTP heartbeat per install

An install is identified by a random identifier generated locally on first run and persisted under the server's `/data` volume, alongside the SQLite database. It is **not** derived from hardware, hostname, user, email, network address or any hashed device attribute: it is a random value that means nothing outside Relego, and an install that loses its volume is simply a new install.

Once per day the server sends one small event to the configured telemetry endpoint, and that event is the entire dataset.

The three questions are answered directly from those events:

| Question | Answer |
|---|---|
| Monthly active installs | Distinct installation identifiers observed in a calendar month |
| Versions in the field | Server version carried by each heartbeat, counted per identifier, latest heartbeat wins |
| Countries of installs | Country derived by the processor from the sending address (see §3) and attached to the same event |

A heartbeat is a statement of the present, not a running history: the current version and the current country are what matter, so a later event supersedes an earlier one for the same identifier with no reconciliation and no backfill. Daily granularity is deliberate — it is the coarsest cadence that still lets an install be counted in a month it was genuinely active in, without turning install monitoring into a request stream.

The payload is exactly three fields, and each exists to serve one of the questions above:

- **Installation identifier** — makes distinct installs countable and makes a month's activity a set rather than a sum.
- **Relego server version** — supplies the version breakdown, and turns a release nobody adopted into a visible fact.
- **CPU architecture** — separates an ARM board from an x86 host without claiming to know an operating system, which changes what a version means in practice because a Pi or NAS install upgrades differently from an x86 server.

Nothing else is added to the event. A field that cannot be tied to one of the three questions is not a small increment; it is a new decision with its own policy change.

**Rationale**: a random local identifier is the only identity that answers MAU without turning the install into a subject. A hardware- or hostname-derived fingerprint would survive a rebuilt `/data` volume, which sounds desirable until it becomes a device attribute the project did not ask permission to hold; an account-based identifier would require the account NFR-02 forbids. Randomness also makes the identifier's meaning explicit: it names one volume and nothing else.

Daily cadence is chosen against two failure modes. Sending on every start would count restarts as activity and make the event volume a function of a bug; sending on every request would make it a function of use. Once per day is coarse enough that a missed send costs nothing and fine enough that a month has many chances to observe an active install, so an install used for one week is still counted in the month it was used.

### 3. Vendor: PostHog Cloud EU

Events are sent to **PostHog Cloud EU**, hosted in AWS `eu-central-1` in Frankfurt, Germany, as an independent EU instance: event data, user data and the product all live on EU infrastructure, so no EU-to-US transfer occurs. PostHog supplies a Data Processing Agreement to Cloud customers on request rather than attaching one automatically, so adopting the processor includes requesting the DPA and keeping it with the project's records; that is what makes a hosted processor acceptable for a project whose policy is written from an EU standpoint rather than an American one.

The free plan allowance used here is 1M product analytics events per month with a single project and one-year retention, and ingestion **stops** at the limit instead of billing the project. The volume expectation is one event per install per day, roughly 30 events per install per month, so the free allowance covers tens of thousands of installs before it is reached. That arithmetic is the reason a hosted product is affordable: the project is not buying analytics capacity, it is fitting under a free tier that the event shape was chosen to respect.

The vendor also supplies the three capabilities this decision cannot build: a continuously running ingestion endpoint, a dashboard with per-version and per-country breakdowns, and GeoIP enrichment on every event. Owning those means owning a database and a service; buying none of them means owning neither.

**Rationale**: the EU instance is the part of the vendor choice that cannot be traded away. A US region would make every heartbeat an EU-to-US transfer of a version and a country — a small transfer the project would still have to disclose and justify, for no return the Frankfurt instance does not already give. The DPA has to be requested rather than assumed, and that is recorded here as a step of the adoption: a disclosure section cannot paper over a processor relationship that has no Article 28 contract behind it.

Affordability here is a consequence of the event shape, not of the vendor's generosity. A page-view or request-shaped integration would exhaust the free allowance with a fraction of the base it was sized for. The free plan stopping at its limit rather than billing matters as much as its size: it turns an unexpected spike into missing data rather than an invoice, which is the only failure a volunteer project can absorb.

### 4. Data minimisation is enforced, not promised

Events are captured as **anonymous events with no person profile**. PostHog therefore stores no per-install profile that could be enriched, linked, exported or deleted, because none is created: the event is the record, and the identifier is not a person.

The project's PostHog configuration enables **"Discard client IP data"**. Raw client IPs are not stored with events; GeoIP enrichment still runs on the IP before it is discarded, so only the derived country survives. The country in the dashboard is therefore a property of the event, and the address it came from does not exist in the dataset.

Never sent: highlights or any passage text, book titles, authors, notes, tags, delivery or inbox addresses, SMTP settings or credentials, tokens, the SQLite database, filesystem paths, hostnames, container IDs, log lines, or any user-supplied string. The list is a boundary of the design, not a filtering step applied before sending: the event shape has no field that could carry any of it.

The installation identifier is the only persistent handle, and it carries no link to a person. It is not comparable to an identifier issued by any other service, and it is not reversible to a host, an account or an email.

**Rationale**: anonymity is a property of the storage model, not a line in a document. Capturing anonymous events with no person profile means there is no object to enrich with a support transcript, to export on request, or to delete when someone asks — the deletion question answers itself because the subject was never created. Discarding client IP data while leaving GeoIP enrichment in place is the same reasoning applied to the one field that could identify a network: enrichment happens before the discard, so the country survives and the address does not.

### 5. Default on, disableable, and inert when it fails

Telemetry is on by default, and a documented configuration switch turns it off. The switch takes effect without a rebuild and is honoured for the lifetime of the install — disabling it is a durable state, not a single skipped send. The project also honours the `DO_NOT_TRACK` convention as an additional opt-out signal; this is a product requirement of the decision, not merely a courtesy of the implementation.

Sending is fire-and-forget. It must never block startup or a request, must never retry beyond the next scheduled heartbeat, and must never log at a level that pollutes the user's logs. A telemetry outage — vendor down, network absent, container without egress — is invisible to the user and to the recap pipeline. The failure mode of the mechanism is silence, and silence is also its off state, so there is nothing for the user to fix and nothing that changes behaviour.

**Rationale**: default-on is the decision that makes the data usable, and it is also the one this ADR must justify rather than assume. An opt-in population is self-selected and cannot be divided by the population that exists, so the count it yields is a count of enthusiasts; with a documented switch, the population is everyone who did not object, which is the population the questions ask about. The switch is what keeps that defensible: documented, durable, and verifiable by watching whether the server still opens the connection.

### 6. Disclosure is part of the decision

`PRIVACY.md` stops claiming the software has no telemetry and instead describes this telemetry in its own section: what is sent, what is never sent, that it is anonymous, that it can be switched off, that PostHog Cloud EU is the processor, that it is hosted in the EU (Frankfurt, Germany) under GDPR with a DPA, and that no data leaves the EU. The policy is the user-facing contract, and the mechanism is only legitimate because the policy names it.

NFR-02 is satisfied, and this ADR is the record of how. "Not mandatory" is read as **user-disableable with disclosure**, not as opt-in — the default is on, the off switch is documented, and the policy says so in plain language. Recording that interpretation here is the point: a future reader comparing NFR-02 to a default-on heartbeat should find the reading stated rather than discover a silent reinterpretation.

**Rationale**: disclosure is a condition of the decision, not an announcement that follows it. A default-on heartbeat the policy does not name is a hidden transfer under NFR-02 no matter how little it carries, so the policy section is written first and the mechanism ships second. Stating the NFR reading in the ADR keeps that order enforceable: a later change to the payload cannot land without the policy changing alongside it.

## Consequences

- **`PRIVACY.md` gains a telemetry section and stops asserting the software has no telemetry.** The policy names PostHog Cloud EU as the processor, states EU/GDPR hosting and the DPA, and enumerates what is never collected. The summary bullets change with it, because a policy whose summary contradicts its body is worse than either version.
- **NFR-02 is met through user-disableable telemetry plus disclosure.** The ADR is the standing interpretation that "not mandatory" means default-on-with-opt-out, so a later reviewer either accepts that reading or changes the requirement deliberately.
- **The project gains a vendor dependency and a third-party egress path from the server.** This must be documented in `docs/ARCHITECTURE.md` and kept limited to the heartbeat: any future use of the same channel is a new decision, not an extension of this one.
- **Numbers are directional, not audited.** The analytics token ships inside an open-source server, so anyone can send fabricated events. The dashboard answers "what is the shape of the install base", never "what is the exact count"; it is a steering instrument, not a metric to quote as fact.
- **The event payload is fixed by this ADR.** Adding a field — an OS, a feature flag, a book count — is a new decision with a policy change, because `PRIVACY.md` enumerates what is sent and these three fields are the whole of it.
- **Telemetry is not a licence check, a kill switch or an update channel.** It reports; it cannot enable or disable a feature, and it is not a path for anything the project wants an install to do. Reading it as more than a count would break the narrow scope §1 sets.
- **A future change of processor or region is a new decision, not a configuration change.** Moving off the EU instance would invalidate the policy's "no data leaves the EU" statement, which is the only sentence this decision asks a reader to trust.
- **Free-plan retention is one year and ingestion stops at 1M events per month.** Long-term trend history is bounded by the retention window, and a sudden popularity spike degrades to missing data rather than a bill — the correct failure direction for a project with no analytics budget.
- **The installation identifier must survive restarts and die with the volume.** It lives in `/data` alongside `relego.db`, so backing up `/data` moves the install's identity with it and destroying `/data` erases it, making the next start a new install. This is consistent with the storage decision in ADR-003, which places the database on the same volume, and with the policy's deletion story.
- **A user who disables telemetry must be able to verify it.** Documentation states the switch, and the server must not attempt to send while it is off: a "disabled" install that still opens a connection is a broken promise in the only way a self-hosted user can detect.
- **Nothing in ADR-003, ADR-004 or ADR-006 changes.** Telemetry runs inside the server container ADR-006 defines, stores its identifier on the SQLite volume ADR-003 describes, and adds no inbound endpoint, so the REST protocol of ADR-004 and the Docker-only distribution model both remain exactly as written.
