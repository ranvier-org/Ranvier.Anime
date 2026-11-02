# Automatic geometry staggering

Execute inline using executing-plans, TDD, SageFs and the existing warm Partas.Build commands.

## Contract

Extend StaggerGrid with Automatic carrying existing `Ranvier.Dom.DOMPointInit option array` positions. Missing positions cause the complete distribution to fall back to flat index distances, matching source. Use double coordinates, including z, and ignore homogeneous w. Reuse the existing origin/easing/reversal/jitter pipeline. Automatic distances normalize by the smallest nonzero absolute distance, not by axis spacing or an invented layout grid.

Add `Stagger.autoElements` over existing Ranvier.Dom.Element arrays. It reads getBoundingClientRect once per relevant target and uses left+width/2 and top+height/2. Respect the total override when reading; missing/null targets fall back. Borrow host elements only during the eager call on their owning thread; retain no geometry service, host or element afterward.

## Verification

- Verify actual Ranvier public API shape in SageFs; semantic project-load failures do not establish absence.
- Generate upstream automatic 2D/3D, uneven spacing, origin/axis, duplicate, incomplete, total override and seeded-transform fixtures. Observe ignored-geometry failures first.
- Prove automatic distance construction and DOM measurement API usage in SageFs before persistence.
- Test DOM adapter measurement centers and read counts with recording interface proxies; actual native host integration remains a separate gate.
- Full normal/Traced suites, XML audit and one fresh review; fix findings with regressions, update scope/evidence and commit.

Typed CSS unit staggering and timeline position integration remain required work.

Verified:67 source stagger fixtures,164/164 full Release tests,164/164 Traced Debug tests and XML audit0. Fresh review found no material issues. Proxy tests prove managed DOM adapter calls and measurements; actual native layout qualification remains outstanding.
