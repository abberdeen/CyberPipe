# Architecture Review Checklist

For each item: the question, why it matters, expected evidence, and done/N/A criteria.

## Boundaries and requirements

- [ ] **Q1.** Is every `REQ-xxx` mapped to a component or marked unresolved?
  *Why:* unowned requirements silently die. *Evidence:* §5 table + runtime scenarios.
  *Done when:* each REQ has an owner row or an ASSUMPTION entry.
- [ ] **Q2.** Are external systems and trust boundaries (`TB-01…06`) complete for every
  ingress/egress in `data-flows.md`? *Why:* unmapped flows are unguarded flows.
  *Evidence:* flow diagram covering all entry points. *Done when:* reviewer cannot name
  an unmapped input.

## Assumptions and hidden constraints

- [ ] **Q3.** Does every major technology have a compatibility-matrix row with status
  ≠ presented-as-verified? *Why:* README claims are not production guarantees.
  *Evidence:* matrix rows for storage/bus/sandbox/models. *Done when:* zero
  *Unverified* rows without a named verification task.
- [ ] **Q4.** Are SQLite single-writer, WAL backup, migration serialization, and
  SQLCipher-provider facts PoC-backed? *Why:* the durability story rests on them.
  *Evidence:* PoC results + version pins. *Done when:* RISK-001/RISK-007 tests green.
- [ ] **Q5.** Is cross-OS isolation equivalence proven per OS or explicitly scoped to
  Windows? *Why:* kernels differ; abstraction ≠ equivalence. *Evidence:* per-OS
  negative suites. *Done when:* RISK-002 retired or ADR-0004 amended.

## Failure, concurrency, recovery

- [ ] **Q6.** Is every runtime scenario paired with its failure path (block, pause,
  kill, crash, cancel)? *Why:* happy-path architectures fail at 3 a.m.
  *Evidence:* §6 failure paths + QAS-REL tests. *Done when:* cancel≠poison,
  crash→policy, overload→bulkhead all tested.
- [ ] **Q7.** Are queue/channel bounds, quotas, and retry ceilings enumerated with
  overflow behavior? *Why:* unbounded anything is an OOM/freeze incident.
  *Evidence:* §8 limits + ArchUnitNET ban. *Done when:* no unbounded channel/stream/
  retry exists without a cap and a test.
- [ ] **Q8.** Do backup/restore/rotation/master-key-loss procedures exist as tested
  runbooks, not paragraphs? *Why:* untested recovery is fiction. *Evidence:*
  `TEST-DUR-01…03` green. *N/A only if:* no persistent state (not the case here).

## Security

- [ ] **Q9.** Does every trust boundary name its *enforced* control and its negative
  test? *Why:* library presence ≠ mitigation. *Evidence:* mitigations matrix.
  *Done when:* all THREAT items have a verification method with a home in CI.
- [ ] **Q10.** Is taint handling end-to-end (tag → RAG filter → tool strip → HITL
  write) covered by one suite, including stored-injection via memory? *Why:*
  delayed injection bypasses point checks. *Evidence:* `TEST-SEC-03` incl. vault/CRM
  fixtures.
- [ ] **Q11.** Are fail-closed defaults specified for guard/vault/sandbox/OSINT
  outages? *Why:* fail-open turns incidents into breaches. *Evidence:* explicit
  per-outage behavior. *Done when:* no "caller decides" without a default-deny.

## Decisions and consistency

- [ ] **Q12.** Is every Accepted ADR backed by evidence or recorded approval, with
  alternatives and revisit triggers? *Why:* accepted-by-inertia decisions rot.
  *Evidence:* ADR index statuses. *Done when:* zero Accepted-without-evidence.
- [ ] **Q13.** Do overview, arc42 text, diagrams, ADRs, and plans agree on names
  (bus, DB paths, cache key, SLO framing)? *Why:* the audit already caught drift once.
  *Evidence:* clean link/name sweep. *Done when:* sweep passes (see checklist footer).
- [ ] **Q14.** Are measurable criteria present (no "fast/secure/scalable" without
  numbers or an explicit open-target mark)? *Why:* unmeasurable DoDs can't gate
  sprints. *Evidence:* QAS scenarios. *Done when:* every quality claim has a
  criterion or an open-target flag.

## Risks foreclosed

- [ ] **Q15.** Does every Critical/High risk have a mitigation, acceptance note, or
  validation plan with an owner? *Why:* discovered-but-unaddressed risks recur.
  *Evidence:* risk register completeness. *Done when:* no orphaned RISK rows.
