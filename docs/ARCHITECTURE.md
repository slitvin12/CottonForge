# Архитектура движка Forge

> Статус: **собрано (0 ошибок, 0 предупреждений), в игре проверено частично** — см. «Что проверено, а что нет». Всё, что ниже помечено «проверить в игре», —
> места, где поведение SR2 известно только по декомпиляции.

Forge — слой `CottonLibrary.Forge` поверх Cotton. Он состоит из трёх уровней:

| Уровень | Папки | Что внутри |
|---|---|---|
| Ядро | `Forge/Core`, `Forge/Fixes` | лог и `Safe`, `Scheduler`, `ForgeSave` (данные модов в сохранении), `LiveConfig`, `Gc`, `ModAssets`, ремонт игры |
| Контент | `Forge/Content`, `Forge/Visual`, `Forge/Gameplay` | `ForgeMod<M>`, `SlimeBuilder`, ларго, спавн, материалы, лица, FX, звук, `World`, `SlimeEvents`, `Movement`, `Aura` |
| Движок | `Forge/Architecture` + подсистемы | контракты (интерфейсы), модули, рантайм поведения и шесть подсистем ниже |

## Каркас

- **`Contracts.cs`** — все публичные интерфейсы движка (точки расширения), см. таблицу ниже.
- **`ForgeModules`** — реестр модулей `IForgeModule { Id, Init, Tick }`. Встроенные модули регистрируются при старте Cotton,
  моды могут добавить свои (`ForgeModules.Register`). Модуль, бросивший 50 исключений в `Tick`, выключается. Любой модуль
  можно отключить настройкой `DisabledModules` (список id через запятую). Команда `modules` показывает состояние.
- **`ActorRuntime`** — управляемая логика на объектах без IL2CPP-инъекции типов: `ActorRuntime.Add(go, IActorLogic)`.
  `Start` — на следующем кадре, `Tick(dt)` — каждый кадр (дальше 70 м от игрока — раз в 0,5 с с накопленным `dt`),
  `Stop` — при снятии или уничтожении объекта. Логика, упавшая 10 раз, снимается, остальные продолжают работать.
  `ActorRuntime.Signal(go, SlimeSignal)` раздаёт события всем `IArchetypeRuntime` объекта.
- **`ForgeSave`** — данные модов в сохранении: `UserData\CottonLibrary\saves\<игра>.json`, ключ-значение
  (`Get/Set`, `GetFloat/GetInt/GetBool`, `Keys(префикс)`), события `Loaded`/`Saving`. Сохранение игры не трогается.

| Модуль (id) | Класс | Хуки игры |
|---|---|---|
| `runtime` | `ActorRuntime` | — |
| `weather` | `ForgeWeather` | опрос `WeatherDirector`, `GlobalWindControl`; Harmony: `LightningStrike.Start` |
| `market` | `PlortMarket` | Harmony: `PlortEconomyDirector.GetTargetValue`, `RegisterSold` |
| `vacpack` | `FireModes` | Harmony: `VacuumItem.Expel(AmmoMetadata, …)`; клавиша `FireModeKey` |
| `hud` | `ForgeHud` | свой Canvas; видимость по `HudUI` |
| `matanim` | `MaterialAnimator` | — |

Без модуля (вызываются из хуков спавна/событий): `Anatomy` и `LargoEngine` (Harmony `SlimeEat.Awake`, генерация ларго),
`GadgetKit` (Harmony `Gadget.Start`), `SlimeEvents` → сигналы `Ate`/`Watered`.

### Интерфейсы

| Интерфейс | Для чего | Где регистрировать |
|---|---|---|
| `IForgeModule` | своя подсистема с `Init`/`Tick` | `ForgeModules.Register` |
| `IActorLogic` | логика на объекте | `ActorRuntime.Add` |
| `IArchetype` / `IArchetypeRuntime` | поведение слайма (тип и экземпляр на слайме) | `Archetypes.Register`, `Anatomy.For(def).Archetype(...)` |
| `ILargoMerger` | шаг слияния анатомии в ларго | `LargoEngine.AddMerger` |
| `IWeatherClassifier` | имя состояния погоды → `WeatherKind` | `ForgeWeather.AddClassifier` |
| `IWeatherReaction` | реакция слайма на погоду | `ForgeWeather.React` |
| `IGadgetLogic` | логика поставленного гаджета | `GadgetKit.Logic` |
| `IDroneProgram` | что делает дрон | `HelperDrone.Spawn`, `DroneStationLogic` |
| `IPriceModel` | цена плорта | `PlortMarket.Register/SetModel`, `DefaultModel` |
| `IMaterialPreset` | рецепт материала | `MaterialPresets.Register` |
| `IFireMode` | режим стрельбы пылесоса | `FireModes.Register` |
| `IHudWidget` | элемент HUD | `ForgeHud.Add` |

### Поток данных

```
спавн слайма ─ SlimeEat.Awake ─ Anatomy.OnSlimeAwake ─(кадр)─ Anatomy.Resolve(def)
                                                         ├ своя AnatomySpec
                                                         └ ларго: LargoEngine.Merge(базы) → мерджеры
                                                       Anatomy.Apply: слои (MeshKit.AddLayer), части, архетипы → ActorRuntime
SlimeEat.EatAndProduce ─ SlimeEvents.RaiseEat ─ ActorRuntime.Signal(Ate, еда)
CalmedByWaterSpray.AddLiquid ─ SlimeEvents.RaiseWater ─ Signal(Watered)
ForgeWeather (раз в 1 с) ─ активные состояния → WeatherKind ─ слаймы под открытым небом ─ Signal(WeatherTick) + реакции
LightningStrike.Start ─ ForgeWeather.LightningWarning/Lightning ─ Signal(Lightning) слаймам в радиусе 30 м
генерация ларго ─ Largos.Process ─ LargoEngine.ProcessDefinition (диета-объединение, голоса)
```

## 1. Ларго и анатомия (`Forge/Anatomy`)

- **`AnatomySpec`** — декларация «из чего состоит слайм»: `Layer(имя, материал, раздутие, грани)` — скиннированный слой
  поверх тела; `Part(имя, go => объект)` — прикреплённые объекты; `Archetype(id|объект, сила)`; `Voices(набор, приоритет)`;
  `NoLargoInheritance()`. Получить: `Anatomy.For(def)` или `Anatomy.For("Имя")`.
- **`LargoEngine`** — слияние двух баз в ларго. Мерджеры по умолчанию: слои, части (масштаб `largoScale`), архетипы (сила
  × `LargoIntensity` = 0,75, без дублей), голос (выше `VoicePriority` побеждает). Диета ларго дополняется объединением диет баз
  (`MergeDiets`, один раз на ларго) — только у ларго с базой-слаймом Forge или с объявленной анатомией; ларго из ванильных
  и чужих слаймов не трогаются, пока не включён `MergeAllLargos`. Свои шаги — `LargoEngine.AddMerger`, пересчёт — `Reprocess()`.
- **`LargoBlend`** — внешность ларго Cotton: шаблон, тело, лицо, цвета в OKLCh, части без дублей, эффекты, radiant-варианты.
  Правила — `LargoStyle` (слайм), `LargoRecipe` (пара), событие `Building` с контекстом `LargoBuild`. Подробно — README,
  «Смешивание ларго». `LargoEngine` работает после него, на готовых ларго.
- **`LargoParity`** — «начинка» ларго как у ванильных, сразу после `LargoBlend` (`CreateCompleteLargo`): ссылки префаба
  на шаблон → на ларго (`Repoint`), компоненты родителей с полями и пересчётом ссылок (`MergeComponents`), группы
  (`JoinLargoGroups`; базы `SlimeBuilder` — `JoinBaseGroups`), остатки шаблона от его второго родителя, анимации от
  родителей, `AppearancesDynamic`, плорты модовых родителей в группах ванильных плортов (`Plorts`: иначе их никто не ест),
  очистка full art, зоны, пустые поля диеты; ссылки пересчитываются и в массивах/списках. Итог — `ResultOf(ларго)` и строка
  `parity` в `blend <ларго>`. Правила — от **`VanillaSlimes`** (`Forge/Content`): снимок ванильных слаймов в Prefix
  `AutoSaveDirector.Awake` (до модов), ленивый `Learn()` — общие группы и компоненты ванильных ларго и баз, группы и
  компоненты «от родителя», группы плортов (`PlortGroups`), доли тела и лица (`PreferBody`/`PreferFace`), чья палитра
  побеждает (`OneParentColors`, `ColorShare`, `ColorRank`, `PreferColors`; у модовых слаймов — из веса цвета), правила
  частей (`PartRules`: ларго-версии, пропадающие части, победители слота) — их берёт `LargoBlend`; зоны, любимая продукция;
  `Describe()` — отчёт.
- **`LargoAudit`** (`Forge/Dev`) — сверка ларго Cotton с ванильными: через `AutoDelay` = 10 с после генерации — строка
  `[largo audit]` в логе и `largo_audit.txt`; `SelfTest` — план движка для каждого ванильного ларго против настоящего
  (тело, лицо, палитра в OKLab, части). Ждёт готовых иконок (до 30 с). Команда `largoaudit`.
- **Архетипы** (`Archetypes.Get(id)`; настройки — `new XArchetype(new XArchetype.Config { … })` или правка `Settings`):

| id | Источник | Поведение |
|---|---|---|
| `quantum` | SR1 | возбуждённый оставляет «эхо»-копии и телепортируется к ним; проходит сквозь стены загона |
| `dervish` | SR1 | вихрь: притягивает и закручивает актёров вокруг себя; сильный ветер тоже запускает вихрь |
| `tangle` | SR1 | голодный тянет лозой (LineRenderer) съедобную еду к себе |
| `mosaic` | SR1 | на солнце искрится бликами (по желанию блики ранят игрока — `GlintDamage`) и успокаивается |
| `glitch` | SR1 | мерцает пиксельным материалом, маскируется под соседа, заражает слаймов (поколения), вода лечит |
| `rad` | SR1 | радиационная аура через `Aura.Irradiate`, по желанию — видимая сфера |
| `quicksilver` | SR1 | рывки к еде, удар током рядом, гроза и молния усиливают, вода разряжает |
| `ringtail` | SR2 | каменеет на солнце (материал `stone`, движение остановлено), оживает в тени и ночью |
| `batty` | SR2 | в темноте прыгает-порхает активнее, днём вялый |
| `flutter` | SR2 | после нектара парит и покачивается в воздухе |
| `yolky` | SR2 | рядом с курами (≥ `MinHens`) неподвижно «высиживает» и производит продукцию |

Своё поведение: класс от `Archetype<TConfig>` + `ArchetypeRuntime` (готовые `Ready`/`Cooldown`, `Own` для временных
объектов, `Swap` для подмены материалов, `OnSignal` для еды/воды/погоды/молнии) → `Archetypes.Register`.

## 2. Погода и окружение (`Forge/Environment`)

- `ForgeWeather.Active` — флаги `WeatherKind` (`Rain`, `Thunder`, `Pollen`, `Wind`, `SlimeRain`, `Snow`, `Fog`, `Heat`, `Other`),
  `States` — сырые имена состояний SR2, `WindIntensity`/`WindDirection`/`Wind`, `Exposed` — слаймов под открытым небом
  (`-1` — не считалось: нет погоды или нет реакций), `FailedDirectors`.
- Опрос в два прохода: сначала состояния всех `WeatherDirector` → `Active` и `Changed` (до реакций, поэтому ошибка реакции их
  не «замораживает»), затем слаймы под открытым небом — снимок `_exposedActorsCache[*].ExposedActors` по индексу (ленивый
  `GetAffectedActors()` только запасной путь: его перечислитель бросал «Collection was modified»); каждый director в своём
  try/catch, ошибка не выключает модуль.
- Классификация **по именам** состояний и паттернов (встроенная таблица ключевых слов: rain, storm, thunder, pollen, wind,
  tornado, snow, fog, …). Если имя у SR2 другое — `ForgeWeather.Map("часть имени", WeatherKind.X)` или свой
  `IWeatherClassifier`. Настоящие имена видно командой `weather`. Проверено в Rainbow Fields: Rain Light, Thunder Heavy,
  Slime Rain, Pollen Heavy, Wind Heavy классифицируются верно; Лава и Блики не проверены.
- Реакции: `ForgeWeather.React(def, WeatherKind.Rain, (слайм, погода, dt) => …)` или `IWeatherReaction` — примерно раз в
  секунду для каждого подходящего слайма не под крышей (`Covered` — луч вверх). Ларго — по желанию.
- События: `Changed(было, стало)`, `LightningWarning(точка, время)`, `Lightning(точка, радиус)`; помощники `InSunlight`,
  `InDarkness` (ночь или крыша), `CloudsBlockSun`.

## 3. Гаджеты и дроны (`Forge/Science`)

- `GadgetKit.Clone("TeleporterPink", "MyBeacon", prefab => …)` — новое определение со своим префабом (префаб лежит в
  неактивном держателе и не появляется в мире). `GadgetKit.Logic(def, go => new …Logic())` — логика на каждом поставленном
  гаджете (и на уже стоящих). `Spawn`, `Placed`, `Key(гаджет, поле)` — ключ сохранения по позиции.
- Готовые логики (`GadgetLogic` — база с `Every`, `GameHours`, `Key`):
  - `ExtractorLogic` — производит по взвешенной таблице каждые `CycleHours` игровых часов, `Cycles` циклов, догоняет пропущенное
    время при загрузке (до `MaxCatchUp`), прогресс в `ForgeSave`;
  - `PlortCollectorLogic` — собирает плорты в радиусе и продаёт по цене рынка (или стягивает к себе);
  - `TeleporterLogic("канал")` — телепорт игрока к следующему телепорту того же канала (в пределах региона);
  - `DroneStationLogic(() => программа)` — держит своего дрона.
- `HelperDrone` — дрон в духе SR1: ожидание → полёт к грузу → перенос → доставка → возврат, батарея, повторный поиск.
  Программы `DronePrograms`: `SellPlorts(радиус)`, `Gather(фильтр, радиус, куда)`, `FeedCorral(еда, точка, радиус)`,
  `Custom(...)`; `HelperDrone.IsFree` не даёт двум дронам взять один груз.

## 4. Рынок плортов (`Forge/Economy`)

- `PlortMarket.Register(плорт, цена, насыщение, модель)` — делает плорт продаваемым (через `Library.MakeSellable`, если ещё
  нет) и назначает модель. `SetModel`, `DefaultModel` (для всех без своей), `Price`, `Sell`, `Entries` (живой рынок),
  `RecentlySold` (память продаж: ×0,5 за игровой день), событие `Sold`.
- Модели (`PriceModels`), применяются к цене SR2 (с её насыщением и шумом): `Vanilla`, `Fixed`, `Volatile(амплитуда, период,
  seed)`, `Trend(в день)`, `Glut(порог, пол)` — дополнительное насыщение, `Scarcity(бонус)`, `Weather(погода, множитель)`,
  `Classic` — цена SR1 (база и линейное насыщение), `Chain(...)` — цепочка, `From(функция)`.
- События рынка: `Boom(плорт|null, ×, дней)`, `Crash(...)`, `EndEvent`; сохраняются в `ForgeSave`, истекают сами.
- Модели работают в целевой цене дня (`GetTargetValue`), поэтому меняют цену **при следующем обновлении рынка**;
  `PlortMarket.Refresh()` просит SR2 пересчитать сразу (через `_nextUpdateTime`, проверить в игре).

## 5. Визуал и звук (`Forge/Visual`)

- `MaterialPresets.Build(id, имя, PresetColors)` и короткие `Jelly`, `Glass`, `Prismatic`, `Neon`, `Metal`, `Glitch`, `Stone` —
  материалы на шейдерах SR2 (клоны `slimePuddleBase`, `envLabGlass`, `HyperDefaultRingtailDefault_Body`, `slimePhosphorBase`,
  `slimeCrystalBase`, `slimeRockBase`), кэшируются и защищены хранителем материалов. Свои рецепты — `Register`.
  Посмотреть: команда `preset <id> [r g b]`.
- `MaterialAnimator` — анимация общих материалов: `Pulse` (цвет/число), `HueCycle` (радуга), `Jitter` (смещение текстуры),
  `Add(материал, (m, t) => …)`. `prismatic` и `glitch` уже анимированы.
- `SoundBank` — группы звуков с автобалансом громкости: `Measure` (RMS/пик), `Normalize` (к `TargetRms`, без клиппинга,
  максимум `MaxGain`), `Balance`, `Add(ключ, клипы)`, `Cue`, `Play`. Работает с клипами, чьи сэмплы читаются (все WAV из
  `ModAssets.Clip`); сжатые клипы игры только измеряются, если Unity позволяет.
- `VoicePack` — пакет голоса слайма: `FromAssets(assets, "vo_glitch")` собирает `vo_glitch_fun*`, `_fear*`, `_damage*`/`_hurt*`,
  `_jump*`; `Balance` выравнивает громкость (`Loudness` — уровень пакета), `Apply(def)` или `ApplyToAnatomy(def, приоритет)` —
  тогда голос наследуют ларго. `Register`/`Get` — пакеты по имени.
- `SlimeIcons` — иконки ларго: иконка шаблона (если это ванильное ларго родителя) или родителя-владельца тела, читается
  через `RenderTexture` (работает и с нечитаемыми текстурами), перекрашивается в палитру ларго (OKLCh, окно оттенков
  `HueWindow` = 38°), значок второго родителя в углу; очередь по `PerFrame` = 2 за кадр; ставится в `icon`, `_icon`
  внешности и педию. Если ларго носит палитру второго родителя, пиксели переносятся по шкале светлоты его иконки (`Ramp`,
  `RampBands` = 5 полос равной площади): иконки освещены и используют цвета палитры по-разному. Иконки, поставленные модом или рецептом (`LargoRecipe.Icon`), не трогаются.

## 6. Пылесос и HUD (`Forge/Player`)

- `VacPack` — слоты (`SlotCount`, `Slot(i)`, `Selected`, `SelectedCount`, `CountOf`), `Add`, `Take`, `TakeAt`, событие `Shot`
  (каждый выстрел: снаряд, тип, точка, направление, `ShootAgain`).
- `FireModes` — альтернативные режимы: `Register(IFireMode)` или `Create(id, подпись, shot => …, фильтр)`, переключение клавишей
  `FireModeKey` (по умолчанию `B`; режим «Normal» — без изменений), `Select`, `Cycle`, событие `Changed`. Готовые (подключаются
  явно `RegisterBuiltins()` или командой `firemode builtins`): `Burst` (очередь из 3 с шагом 0,14 с, потом пауза 0,6 с —
  при зажатом курке не утраивает поток), `Spread` (веер из 3; дробины стартуют рядом и 0,5 с не сталкиваются с игроком и
  друг с другом), `Power` (×1,3, не быстрее 55 м/с), `Lob`, `Pulse` (толчок перед игроком + через 0,4 с ударная волна там,
  где снаряд). `FireModes.Push` и `FireModes.IgnorePlayer` доступны для своих режимов.
- `VirtualAmmo` — дополнительные слоты Forge: `Define(id, подпись, вместимость, фильтр)`, `Stash` (выбранный слот пылесоса →
  виртуальный), `Unstash`, `Shoot`; хранятся в сохранении, показываются счётчиком в HUD.
- `ForgeHud` — свой оверлей-Canvas (1920×1080), восемь якорей, виджеты складываются стопкой, видны только когда виден HUD
  игры (не в меню и не на паузе). Шрифт берётся из HUD SR2. `Add`, `Remove`, `Get`, `Toast(текст, сек)`, `Style`, `Enabled`.
  Виджеты: `HudBar` (полоска 0..1), `HudCounter` (панель с текстом), `HudText`; свои — от `HudWidget` или `IHudWidget`;
  для разметки — `ForgeHud.Rect/Fill/Panel/Label`. `AvoidGameHud` (вкл.): раз в 2 с измеряются видимые элементы HUD SR2 у
  верхнего и нижнего края, и стопки угловых/центральных якорей начинаются за ними (`Reserved(якорь)`, `DescribeReserved()`);
  средние якоря (`MiddleLeft/MiddleRight`) не сдвигаются — там стоят тестовые виджеты и счётчики виртуальных слотов.

## Настройки и команды

- `[CottonLibrary]`: `DisabledModules` (""), `FireModeKey` ("B"), `SmartLargos` (true), `LargoRadiants` (true), `LargoParity` (true),
  `LargoIcons` (true).
- Dev-консоль: `help [команда|префикс]`, `modules`, `arch [list | <id> [сила] | off [all | <радиус>]]` (на слайма перед
  камерой; `off all` — со всех, `off <радиус>` — в радиусе от игрока), `anatomy <слайм>`, `weather` (видно, если модуль
  выключен; `exposed n/a` — не считалось),
  `market [плорт] | boom|crash <плорт|all> <×> <дней> | model <плорт> <vanilla|volatile|glut|classic|off> | refresh`,
  `drone sell|gather [радиус] | list | clear` (дрон висит сбоку от игрока, ~3,7 м над землёй, не закрывая обзор),
  `firemode [id|normal|builtins]`, `ammo [stash|unstash|shoot <слот>]`, `hud [test|clear|off|on]` (печатает и занятые HUD
  игры края), `preset <id> [r g b] | clear`, `save [flush]`, `layers` (слои актёров и «не земли»), `largoaudit [<ларго>|all|vanilla|learn]`.

## Влияние на игру без модов-пользователей

Пока моды не используют движок, он ничего не меняет: модели цен и события рынка не заданы — цена SR2 возвращается как есть;
режимов огня нет — выстрел не меняется; анатомия не объявлена — на слаймов ничего не вешается; логик гаджетов нет — патч
`Gadget.Start` сразу выходит; HUD без виджетов не создаёт Canvas. Остаются только опрос погоды (раз в 1 с) и события.

## Что проверено, а что нет

- Проверено: сборка форка и примера (0 ошибок, 0 предупреждений); сигнатуры всех методов игры, на которые ставятся патчи и
  которые вызываются, сверены с декомпиляцией SR2 1.3.0.
- Проверено в игре (drop-in вместо 0.3.1 с ClassicSlimes 1.1.0): загрузка (40 патчей, 0 ошибок), ларго и их ИИ, ID в
  сохранении, `modules`, `arch list`, `arch quantum`, `arch rad` (облучение и урон SR2), `weather` (имена пяти состояний
  Rainbow Fields, ветер), `market` (`boom` + `refresh` меняют цену), `ForgeSave` (события рынка истекают по дням и
  переживают полный перезапуск игры), `save`/`save flush`, `anatomy`, переключение режимов огня клавишей B, `ammo`,
  `hud test/clear`, `preset`, диагностика (`dump`, `hier`, `report` и др.).
- Исправлено по итогам проверки (ждёт повторной проверки): поиск актёров видел только слой слаймов (15), а плорты на слое 11 —
  `drone sell`, сборщики и пища архетипов их не находили; `spawn` падал (NRE в `InstantiateActor` из-за нового параметра
  `Nullable<AmmoMetadata>`) — теперь спавн идёт через модель актёра, как в самой игре; консольный дрон закрывал обзор; лишняя
  запятая в `firemode`; тестовая полоска HUD перекрывала здоровье игры; `ForgeWeather` падал на перечислении
  `GetAffectedActors()` («Collection was modified») и после 50 ошибок выключался — погодные сигналы слаймам не доходили;
  режимы `burst`/`spread`/`power`/`pulse` переделаны по отзыву; `arch off all|<радиус>`; `help <команда>`.
- Проверено в игре с ClassicSlimes (60 ларго): смешивание (`LargoBlend`, стили, radiant-варианты), паритет (`LargoParity`,
  `VanillaSlimes`), иконки (`SlimeIcons`), аудит — `[largo audit] 60 generated largos, 0 with problems`; самопроверка
  `largoaudit vanilla`: тело 42/46, лицо 92/104, палитра dE 0,025, части 97 % (на тех же ларго, по которым выучено). По
  итогам исправлены: иконки не делались (сравнение `null` с обёрткой), ларго в `Slimes` до сотни раз (`SaveLibrary`),
  родитель не становился новым ларго (`RefreshEatmap` до `RefreshIndexes`), плорты модовых слаймов никто не ел.
- Не проверено: рецепты ларго, дроны и `spawn` после исправления, погодные реакции архетипов (dervish, quicksilver и др.), архетипы mosaic,
  glitch, ringtail, quicksilver, batty, flutter, yolky, tangle, Лава и Блики, мультиплеер. `arch rad` даёт меньше радиации, чем
  `RadsPerSecond × время` (SR2 сам снимает ~1–2 рад/с и, похоже, иначе считает `Absorb`) — не разобрано.

## Ограничения

- Виртуальные слоты не добавляют настоящие слоты в UI пылесоса SR2 — это отдельное хранилище с HUD-счётчиком.
- Дроны Forge не связаны с системой дронов SR2 и летят напрямую (без поиска пути) — держите радиус работы на открытом месте.
- Погода классифицируется по именам состояний; при другом наименовании в SR2 нужен `ForgeWeather.Map`.
- Изменения цен применяются при следующем обновлении рынка; `Refresh` ускоряет, но зависит от внутреннего расписания SR2.
- Режимы огня меняют скорость снаряда после `Expel`; если SR2 задаёт скорость позже (в следующем кадре), часть режимов
  придётся перенести на отложенный вызов.
- Телепорт-гаджеты работают в пределах одного региона.
- Табло рынка показывает 25 из 29 плортов («no room for: RadPlort, QuantumPlort, MosaicPlort, QuicksilverPlort») — так же,
  как в 0.3.1 (на табло не хватает мест; продажа этих плортов отдельно не проверялась).
- Закешированный хэш-ID определения не сбрасывается (на нём держатся словари игры) — дубли показывает `largoaudit`.
- Иконки ларго — перекраска готовых иконок, а не рендер модели. Ларго-версия тела Saber не выучена (мало примеров).
- Мультиплеер (SR2MP) не синхронизирует ничего из движка: архетипы, погодные реакции, дроны, рынок и HUD — локальные.

## Пример

```csharp
Anatomy.For(def)
    .Archetype("flutter", 0.6f)
    .Layer("Glaze", () => MaterialPresets.Glass("Glaze", new PresetColors(top, mid, bottom)), 0.03f);
ForgeWeather.React(def, WeatherKind.Rain, (s, kind, dt) => { if (World.Chance(0.15f * dt)) s.Rb.AddForce(Vector3.up * 5f, ForceMode.VelocityChange); });
PlortMarket.Register(plort, 40, 12, PriceModels.Chain(PriceModels.Volatile(0.2f, 4f), PriceModels.Glut(40f, 0.4f)));
var beacon = GadgetKit.Clone("TeleporterPink", "MyBeacon");
GadgetKit.Logic(beacon, go => new TeleporterLogic("beacon"));
FireModes.Register(FireModes.Spread(2, 12f));
ForgeHud.Add(new HudCounter("my.price", HudAnchor.TopRight, () => $"Plort: {PlortMarket.Price(plort)}"));
```

Полный пример — `examples/ForgeExample/Main.cs` (`UseEngine`).
