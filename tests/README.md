# Review verification assets

`check_repository.py` performs source-only project inclusion, JSON/XML, README consistency, dependency and English-text
checks. It does not compile or execute the mod:

```text
python tests/check_repository.py
```

`Regression/` contains a maintainer-invoked boundary regression runner and deterministic game doubles. It is not a
Unity or Valheim runtime test, and it was not built or executed during the 2026-09-08 review. There is no automatic
build or test workflow added by this change. The detailed coverage and remaining release checks are recorded in
`docs/reviews/2026-09-07-repository-review.md`.
