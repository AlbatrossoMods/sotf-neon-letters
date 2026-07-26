# Validated Review Fixes Implementation Plan

> **For Codex:** Execute this plan task by task with TDD; stop at every stated stop condition instead of guessing.

**Goal:** Исправить четыре подтверждённых дефекта сборки и восстановления состояния, не меняя установленные пользовательские и wire/save-контракты.

**Architecture:** Изолировать MSBuild intermediate output на уровне репозитория; свести fallback rollback к одному идемпотентному lifecycle-пути; вынести bounded single-player restore и snapshot batch protocol в чистые детерминированные координаторы; оставить Unity/runtime-классы тонкими адаптерами.

**Tech Stack:** C#/.NET, MSBuild, Unity, текущий contract-test harness, Stryker.NET.

## Неподвижные контракты и границы

- Raw `E`/raycast-поведение, UI и немедленное закрытие окна по `Apply` не меняются.
- Формат/версия save schema и существующий live `ColorState` v1 остаются byte-for-byte совместимыми.
- Существующие тесты и `tests/SOTFNeonLetters.ContractTests/Program.cs` не редактируются; добавляются только перечисленные test-файлы.
- Не входят в scope: `E`, unregister, unrelated smells, `Apply` ACK и rate limiting.
- Любое расхождение фактического API с предполагаемыми точками интеграции — STOP: зафиксировать сигнатуры и скорректировать план до production-правок.
- Любая необходимость изменить перечисленное пользовательское поведение или wire/save-контракт — STOP: запросить product-решение, не подменять его техническим предположением.

### Task A: Изолировать `obj` двух проектов без изменения outputs

**Files:**

- Create: `Directory.Build.props`
- Create: `tools/test-build-order.sh`
- Verify only: `SOTFNeonLetters.csproj`
- Verify only: `SOTFNeonLetters.Core.csproj`

**New test file/cases:**

- `tools/test-build-order.sh`: Core → main и main → Core из чистого intermediate state.
- В обоих порядках проверить раздельные `BaseIntermediateOutputPath`/`MSBuildProjectExtensionsPath`, успешную компиляцию и неизменные `OutputPath`/`TargetPath` обоих проектов.

**RED:** Сначала добавить исполняемый gate и запустить `./tools/test-build-order.sh`. Он должен падать, потому что проекты используют общий `obj` и порядок сборки способен переиспользовать чужие assets/generated files; зафиксировать текущие `OutputPath`/`TargetPath` как regression expectations до добавления props.

**Minimal GREEN:** В `Directory.Build.props` задать project-scoped intermediate path через `$(MSBuildProjectName)` и согласованный `MSBuildProjectExtensionsPath`. Не менять `BaseOutputPath`, оба `.csproj` или конечные bin/artifact paths. Скрипт должен завершаться при первой ошибке и проверять оба порядка.

**Verification:**

- Targeted: `./tools/test-build-order.sh`
- Full: `./tools/test-all.sh`
- Build: `.tools/dotnet-6/dotnet build SOTFNeonLetters.csproj -c Debug -p:DisableCopyToGame=true`
- Build: `.tools/dotnet-6/dotnet build SOTFNeonLetters.csproj -c Release -t:Compile -p:DisableCopyToGame=true`

**Commit:** `build: isolate intermediate output per project`

### Task B: Гарантировать exact-once rollback активированного fallback

**Files:**

- Modify: `NeonLetterMultiplayerPersistencePolicy.cs`
- Modify: `NeonLetterMultiplayerSaveRuntime.cs`
- Create: `tests/SOTFNeonLetters.ContractTests/FallbackRollbackRegressionTests.cs`

**New test file/cases:**

- Ошибка после успешной активации fallback вызывает rollback ровно один раз.
- Повторные terminal callbacks (`disconnect`/`shutdown`/`dispose`) после той же ошибки не вызывают второй rollback.
- Ошибка до активации fallback и нормальный путь без fallback не вызывают rollback.
- Ошибка rollback не маскирует исходную failure semantics согласно существующему runtime-контракту.

**RED:** `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj --filter FullyQualifiedName~FallbackRollbackRegressionTests`. Ожидаем падение exact-once кейсов: rollback сейчас привязан к нескольким terminal-путям либо не защищён единым lifecycle gate.

**Minimal GREEN:** После подтверждённой активации armed-состояние принадлежит одному runtime lifecycle; все terminal-пути вызывают один общий rollback helper, который атомарно снимает armed-состояние до обращения к policy. Не добавлять новый публичный API, ACK, unregister-поведение или общий cleanup-рефакторинг.

**Verification:**

- Targeted: команда RED повторно.
- Full contract tests: `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`
- Full repository: `./tools/test-all.sh`

**Commit:** `fix: roll back multiplayer fallback exactly once`

### Task C: Добавить bounded single-player restore retry

**Files:**

- Create: `NeonLetterSinglePlayerRestoreCoordinator.cs`
- Modify: `SOTFNeonLetters.Core.csproj`
- Modify: `NeonLetterColorRuntime.cs`
- Modify only if required by the confirmed API: `NeonLetterColorPersistencePolicy.cs`
- Create: `tests/SOTFNeonLetters.ContractTests/SinglePlayerRestoreRetryTests.cs`

**New test file/cases:**

- Один tick пытается восстановить не более 16 entries, а остаток переносится на следующие ticks.
- Временно недоступная цель повторяется и успешно восстанавливается до истечения 15 секунд.
- На границе 15 секунд pending entry истекает и больше не применяется.
- Успешная entry удаляется из pending и не применяется повторно; завершённая/истёкшая batch перестаёт планировать работу.

**RED:** `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj --filter FullyQualifiedName~SinglePlayerRestoreRetryTests`. Ожидаем падение, потому что текущий runtime не имеет тестируемого bounded retry lifecycle с лимитами 16/tick и 15 секунд.

**Minimal GREEN:** Реализовать чистый coordinator с injected/current monotonic time, 15-second deadline, стабильной очередью pending entries и максимум 16 попытками за tick. `NeonLetterColorRuntime` только начинает restore и передаёт tick/apply-result; policy менять лишь если существующая сигнатура не позволяет различить success и retryable unavailable. Save schema не менять.

**Verification:**

- Targeted: команда RED повторно.
- Full contract tests: `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`
- Full repository: `./tools/test-all.sh`

**Commit:** `fix: bound single-player restore retries`

### Task D: Сделать snapshot completeness отдельным request/frame state machine

**Files:**

- Create: `NeonLetterSnapshotBatchCoordinator.cs`
- Modify: `SOTFNeonLetters.Core.csproj`
- Modify: `NeonLetterMultiplayerRuntime.cs`
- Modify only if required by the confirmed API: `NeonLetterSnapshotRequestScheduler.cs`
- Create: `tests/SOTFNeonLetters.ContractTests/SnapshotBatchProtocolTests.cs`

**New test file/cases:**

- Request state принимает frames только для текущего `requestId`; новый request делает предыдущие frames stale.
- Frame state требует последовательность `Begin(requestId, count)` → уникальные `Entry(requestId, index, value)` → `Complete(requestId, count)`.
- Entry до Begin, неверный `requestId`, индекс вне диапазона, duplicate index, mismatched Complete count и incomplete batch не публикуются.
- Complete публикует полный batch атомарно только при совпадении declared/expected/unique-entry counts.
- Live `ColorState` v1, полученный после Begin и до Complete, побеждает stale snapshot для той же буквы; остальные полные snapshot entries применяются.
- Existing live `ColorState` v1 encoding/decoding и immediate `Apply` close остаются без изменений.

**RED:** `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj --filter FullyQualifiedName~SnapshotBatchProtocolTests`. Ожидаем падение: текущая логика не разделяет lifecycle исходящего request и completeness входящих frames и не защищает live update от позднего snapshot Complete.

**Minimal GREEN:** Чистый coordinator хранит отдельно текущий request и текущий frame batch. Scheduler выдаёт/отслеживает монотонный `requestId`; snapshot-only wire frames несут `requestId` и payload `Begin(count)`, `Entry(index)`, `Complete(count)`. На Begin фиксируется live-revision watermark; Complete применяет только полный matching batch и пропускает entries, для которых после watermark пришёл live update. Live `ColorState` v1 не расширять и не переименовывать; не добавлять ACK или rate limiting.

**Verification:**

- Targeted: команда RED повторно.
- Full contract tests: `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj`
- Full repository: `./tools/test-all.sh`

**Commit:** `fix: validate complete snapshot batches by request`

## Final gate

После Task D рабочее дерево должно содержать только перечисленные изменения. Выполнить:

1. `git diff --check` и ручной scoped diff review: никаких изменений raw `E`/raycast, UI/immediate `Apply` close, save schema, live `ColorState` v1 и существующих tests.
2. Новые targeted suites, затем `.tools/dotnet-6/dotnet test tests/SOTFNeonLetters.ContractTests/SOTFNeonLetters.ContractTests.csproj` и `./tools/test-all.sh`.
3. `.tools/dotnet-6/dotnet build SOTFNeonLetters.csproj -c Debug -p:DisableCopyToGame=true` и `.tools/dotnet-6/dotnet build SOTFNeonLetters.csproj -c Release -t:Compile -p:DisableCopyToGame=true`.
4. Unity gates: `./tools/build-unity-assets.sh`, `./tools/test-unity-assets.sh`, `./tools/test-clean-unity-reproducibility.sh` (`UNITY_EDITOR_PATH` override допустим). Если canonical harness отсутствует или требует неизвестный локальный Unity path — STOP, путь не изобретать.
5. `./tools/test-mutation.sh`: pinned Stryker.NET 3.10.0, thresholds high 85 / low 82 / break 82. Mutation score ≥ 82 без synthetic padding, бессодержательных assertions или тестов, написанных только для убийства мутантов.

Не считать работу завершённой при любом failed/skipped обязательном gate; сохранить диагностику и остановиться на минимальном воспроизводимом расхождении.

## As-built review deltas

Task D в итоговой реализации использует общий wire sanity bound 65,536, глобальный бюджет 256 frames/update, отложенные freeze snapshot и отправку `Begin`, а также линейный `ReceiveBatch`; существующие тесты не изменялись.
