## Summary

<!-- What does this change do, and why? -->

## Linked issue

Closes #

## How was this tested?

<!-- Commands you ran (for example `dotnet test TeamsJarvis.slnx -c Release`), the provider and source mode you used, and any manual checks. Simulation mode is fine for UI and pipeline changes. -->

## Checklist

- [ ] The solution builds and all tests pass (`dotnet build` and `dotnet test` on `TeamsJarvis.slnx`).
- [ ] New or changed logic has tests.
- [ ] Docs are updated (README, `COST-OPTIMIZATION.md` or installer docs) if behaviour, settings or data handling changed.
- [ ] No secrets: no API keys, tokens, `settings.json`, `jarvis.db` or logs.
- [ ] No real chat data: test data, screenshots and examples use fictional names (for example, Contoso).
- [ ] Native tools stay read-only and the app still never sends Teams messages.
