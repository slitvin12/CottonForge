# CottonForge 0.3.3 — форк Cotton Library

**CottonForge** — форк [Cotton Library](https://gitlab.com/thean-mcgarity/CottonLibrary) (автор — PinkTarr) для Slime Rancher 2.
Это **прямая замена** `CottonLibrary.dll`: то же имя сборки, те же пространства имён и весь публичный API версии 0.3.1.
Моды, собранные под оригинальный Cotton 0.3.1, работают без пересборки. Сверх оригинала в форке есть:

- исправленные баги Cotton — прямо в исходниках;
- слой **Forge** (`CottonLibrary.Forge`): декларативное API для слаймов и помощники для модов (бывший SlimeForge);
- защищённое применение Harmony-патчей: каждый класс патчится отдельно, поэтому один сломанный патч не отключает остальные.

> Статус: **собрано (0 ошибок, 0 предупреждений), в игре проверено частично** — ларго (смешивание, паритет, иконки, аудит)
> проверены с ClassicSlimes; что ещё не проверено — [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), «Что проверено, а что нет».

Писалось под SR2 1.3.0, Unity 6000.0.73f1 (HDRP, IL2CPP), MelonLoader 0.7.3 и Il2CppInterop 1.5.1.

## Откуда исходники

Публичный репозиторий Cotton заканчивается на 0.3.0 (июль 2025) и незаконченной 0.4.0. Он не собирается под текущий API
игры (`SavedGame` → `GameV11`, `EconomyDirector` → `PlortEconomyDirector` и т. д.). Выпущенная 0.3.1 собрана из
неопубликованных исходников. Поэтому форк устроен так:

1. Ветка `forge` начинается от тега `v0.3.0`, история upstream сохранена (remote `upstream`).
2. Код 0.3.1 восстановлен из `CottonLibrary.dll` (ILSpy) и разложен по файлам upstream: `Functions/*.cs`, `Patches/*.cs` и т. д.
   XML-комментарии перенесены из исходников 0.3.0 там, где методы совпадают.
3. Проверено: пересобранная DLL даёт **те же публичные объявления**, что и оригинальная 0.3.1 (0 отличий). Форк
   с исправлениями и Forge не убирает ни одного объявления (+348 новых).

## Установка (для игрока)

Нужны Slime Rancher 2 и MelonLoader 0.7+ (как для любых Cotton-модов).

1. Скачать `CottonLibrary.dll` из [релиза](https://github.com/slitvin12/CottonForge/releases/latest).
2. Если в `Slime Rancher 2\Mods` уже есть оригинальный `CottonLibrary.dll` — убрать его из `Mods` (или переименовать
   в `CottonLibrary.dll.orig`): два Cotton одновременно ставить нельзя.
3. Положить `CottonLibrary.dll` из CottonForge в `Mods` (и при желании `CottonLibrary.xml` — подсказки в IDE).
4. В логе MelonLoader: `CottonForge 0.3.3 (fork of Cotton Library 0.3.1; build 0.3.3+<ревизия>, assembly 1.0.0.0): patches N on, 0 off, 0 failed ...`.

Почему файл называется `CottonLibrary.dll`: это прямая замена — все Cotton-моды ссылаются на сборку
`CottonLibrary` (версия 1.0.0.0), поэтому имя сборки и файла не меняются. В списке модов MelonLoader и в логе она
видна как **CottonForge**. Настройки остались в разделе `[CottonLibrary]` файла `UserData\MelonPreferences.cfg`.

Откат — вернуть оригинальный `CottonLibrary.dll` на место.

## Сборка

```cmd
dotnet build CottonLibrary.csproj -c Release                  :: bin\Release\net6.0\CottonLibrary.dll
dotnet build CottonLibrary.csproj -c Release -p:Deploy=true   :: + копия в Mods (оригинал один раз сохраняется как .orig)
```

Версии: `AssemblyVersion` всегда `1.0.0.0` (моды собраны против неё — не понижать и не менять), версия форка — в
`FileVersion` (`0.3.3.0`) и `InformationalVersion` (`0.3.3+<git short rev>`, суффикс `.dirty` — собрано с незакоммиченными
правками). Тот же идентификатор пишется в лог при старте и в команде `info` (`ForgeCore.Build`).

Ссылки берутся из `Libs\` (как в upstream), если там есть сборки, иначе из папки игры (`-p:SR2Dir=...`, по умолчанию
`D:\SteamLibrary\steamapps\common\Slime Rancher 2`). `TestMod`, `ColorSlimes`, `PureLargosSR2` (примеры upstream)
и `examples` в библиотеку не входят.

## Исправления

В исходниках Cotton:

| Баг 0.3.1 | Исправление |
|---|---|
| `LocalizationRepair`: таймер не сбрасывается — каждый кадр заново читается CSV и пересоздаются все строки; при добавлении строки цикл падает с «Collection was modified» | снимок строк, 32 за кадр, полная перезагрузка только при смене языка или раз в `LocalizationInterval` |
| `AddLanguages`: пустая ячейка CSV затирает английский текст пустой строкой | пустая ячейка = английский текст |
| `AddLanguages` после первого `LoadLanguage`: новые английские ключи не попадают в кэш `defaultLang` | кэш сбрасывается, язык перезагружается |
| `AddLanguages` без встроенного CSV — `ArgumentNullException` | понятная ошибка в логе |
| `LoadLanguage` без английской таблицы — `KeyNotFoundException` | пустая таблица по умолчанию |
| `AddToGroup` добавляет дубли (например, `MakeVaccable` после `CreatePlortType`), при неверном имени группы — NRE | проверка членства в ассете и рантайм-группе, предупреждение вместо NRE |
| `AddSlimeFoodGroup` добавляет группу дважды | один раз и без повторов |
| `INTERNAL_GetSloomberPalette` читает `_SloomberMiddleColor`, которого нет в шейдере SR2 (2 ошибки `GetColor` на каждого ларго) | проверка `HasProperty`, защита от null |
| `AutoLargosLoaded()` объявлен, но никогда не вызывается | вызывается у всех Cotton-модов после генерации ларго |
| Один упавший Harmony-патч останавливает `PatchAll` | патчи применяются по одному с логом ошибки |
| `MergeStructures`: копия структуры делит с родителем массив `DefaultMaterials` — перекраска ларго перекрашивает родителя | у копии свой массив (и новый движок ларго, см. ниже) |
| `CreateCompleteLargo` всегда удаляет `RockSlimeRoll` и `DamagePlayerOnTouch` — ларго Rock не катятся | при `SmartLargos` удаляются, только если их нет у родителей |
| `CreateCompleteLargo` обновляет карты еды родителей до `RefreshIndexes` — родитель не превращается в своё новое ларго (Mosaic + Sloomber давал старое) | `RefreshEatmap` родителей после `RefreshIndexes` |
| `SaveLibrary`: проверка `Slimes.Contains` сравнивает обёртки IL2CPP, а не объекты — ларго попадало в `SlimeDefinitions.Slimes` до сотни раз | сравнение по указателю |
| `SaveLibrary` (0.3.3): рассинхронизация `_primaryIndex.Length` и `_reverseIndex.Count`, запись строк `AddString` без write-barrier, потеря `Gc.Array<T>` проверки value-type | единый индекс из `_primaryIndex.Length`, write-barrier `StringArrayAppend`, проверка `il2cpp_class_is_valuetype` |
| `PediaLibrary` (0.3.3): клонированные `PediaEntry` не задавали `_persistenceId` / `DontDestroyOnLoad` и сбрасывались на пресет `pink` при перезагрузке | прописывается `_persistenceId`, `DontDestroyOnLoad` и автовосстановление `_pediaEntryLookup` |
| `SlimeLibrary` / `LargoBlend` (0.3.3): `DoesLargoComboExist` искал подстроку вместо пары, `CreateGordoType` без `DontDestroyOnLoad`, кросс-слот radiant у не-radiant родителя | точное совпадение `AB`/`BA`, обратный alias `SlimeDefinition.BA`, `DontDestroyOnLoad` для гордо и раздельные radiant-слоты |

Патчи методов игры (`Forge/Fixes/GameFixes.cs`, включаются в настройках):

| Проблема | Исправление | Настройка |
|---|---|---|
| Слайм попадает в `SlimeDefinitions.Slimes` несколько раз (тысячи ошибок «same key» на каждом `RefreshIndexes`) | дубли убираются перед `RefreshIndexes` | `FixDuplicateSlimes` |
| У ларго Cotton сломаны `SlimeModules` и список поведений `SubbehaviourPlexer` (стоят, сыплют NRE) | ремонт после генерации и при первой загрузке сохранения | `FixLargoAI` |
| GC во время генерации контента (`AutoSaveDirector.Awake`) — редкие вылеты `0xc0000005` | пауза GC на этот кадр | `SafeGc` |
| Сохранение ссылается на незарегистрированного слайма (включая `LostRadiantIdQueue`), пропавший radiant-вариант или перезаписанный `_pediaEntryLookup` | очистка неизвестных radiant-записей (включая `LostRadiantIdQueue`/`LostRadiantTimeQueue`), защита `SlimeRadiant.SetRadiantAppearance` и обновление `_pediaEntryLookup` | — |

Известное, оставлено как в 0.3.1: у `RefreshEatmapPatch` нет атрибута `[HarmonyPatch]` на классе, поэтому ни Harmony, ни форк
его не применяют. Если включить, поменяется поведение диет — сначала нужно проверить в игре.

Исправления внутри `SlimeBuilder` (только для слаймов Forge): `CreateSlimeDef` оставляет хвосты Pink — общий материал
последней структуры, палитру, полную картинку пэдии, radiant-вариант. `MakeSpawnableInZones` добавляет отдельные условия спавна,
и слайм появляется своей группой независимо от весов. Forge заменяет это на `Spawns`: слайм входит в дикие наборы зоны с весом,
дающим ровно заданную долю.

## Forge: быстрый старт

```csharp
using CottonLibrary;
using CottonLibrary.Forge;

[assembly: MelonInfo(typeof(MySlimes.Main), "MySlimes", "1.0.0", "me")]
[assembly: MelonGame("MonomiPark", "SlimeRancher2")]
[assembly: MelonAdditionalDependencies("CottonLibrary")]

public class Main : ForgeMod<Main>
{
    protected override void DeclareContent()
    {
        Slime("Sunny")
            .Colors("#FFE45C", "#FFC21A", "#E08A00")     // верх, середина, низ (+ необязательный цвет ammo)
            .Eats(FoodGroups.Fruits)
            .Favorite("Pogo")                            // подстроки имени, первая найденная еда
            .Toys("BeachBall", "Ball")
            .Plort(25, 10)                               // цена, насыщение рынка
            .Largo()                                     // Cotton сгенерирует ларго со всеми базовыми слаймами
            .Spawn(Library.SpawnLocations.RainbowFields, 0.05f)
            .Customize(b => b.Body.Flt("_GlossPower", 0.6f))
            .Behaviour<SunnyHop>();                      // компонент на каждом заспавненном слайме и его ларго
    }
}
```

В `.csproj`: `<Reference Include="$(SR2Dir)\Mods\CottonLibrary.dll" Private="false" />` и
`<EmbeddedResource Include="Assets\**\*" />`. Полный пример — `examples/ForgeExample`: слайм, конфиг, поведение,
dev-команда и CSV на английском и русском.

`Assets\localized.csv` (UTF-8, первая строка `key,en,ru,...`):

| ключ | что это |
|---|---|
| `l.sunny_slime`, `l.sunny_plort` | имена слайма и плорта |
| `m.intro.sunny_slime`, `m.slimeology.sunny_slime`, `m.risks.sunny_slime`, `m.plortonomics.sunny_slime` | пэдия |
| `l.sunny_pink_largo` (порядок любой) | имя ларго; если ключа нет — имя от Cotton |

Иконки: `Assets\iconSlimeSunny.png`, `Assets\iconPlortSunny.png` (или `.Icon(...)` / `.PlortIcon(...)`). Без них берётся
иконка шаблона и пишется предупреждение.

### Жизненный цикл `ForgeMod<M>`

`ForgeMod<M>` — это `CottonModInstance<M>`. Методы `OnEarlyInitializeMelon`, `SaveDirectorLoaded`, `OnSavedGameLoaded`
закрыты (`sealed`), вместо них:

1. `OnEarlyInitializeMelon` — `Assets\localized.csv` загружается в Cotton (`LocalizationFile` можно переопределить или
   вернуть `null`). **Не вызывайте `Library.AddLanguages` сами.**
2. `SaveDirectorLoaded` (главное меню, один раз, GC на паузе) — `DeclareContent()` → создание слаймов → связи → обновление
   диет → имена ларго → `OnContentCreated()`.
3. Cotton генерирует ларго → Forge чинит их → `AutoLargosLoaded()` у всех Cotton-модов.
4. Загрузка сохранения — `OnFirstGameLoaded()` (один раз за сессию) и `OnGameLoaded()` (каждый раз).

### API Forge

| Модуль | Что делает |
|---|---|
| `SlimeBuilder` | `Key`, `Colors`, `Icon`, `PlortIcon`, `PediaArt`, `Template`, `Plort`/`NoPlort`, `Eats(FoodGroups | группы)`, `Favorite`, `AlsoEats`, `Toys`, `Largo`/`NoLargo`, `Spawn(зоны, доля / Func / MelonPreferences_Entry)`, `Enabled`, `KeepRadiant`, `Customize`, `CustomizeLargo`, `Behaviour<T>`, `OnSpawn`, `Voices(fun, fear, damage, jump)` (свои голоса из WAV). Свойства после создания: `Def`, `PlortType`, `Appearance`, `Body`, `IconSprite`, `Largos`. Можно и без `ForgeMod`: `new SlimeBuilder("X").…Create()` в `SaveDirectorLoaded`. |
| `SlimeRegistry`, `Largos` | все слаймы Forge (`All`, `Find`, `IsForge`), ларго слайма (`Largos.Of`), ручной ремонт (`FixModules`, `FixPlexer`) |
| `Idents` | быстрый поиск типов (`Find`, `FindByHints(подсказки, IdentKind, группа)`), группы (`Group`, `InGroup`, `AddToGroup` без дублей — и ассет, и рантайм-группа), `RegisterForSave`, `DisplayName`, `RefreshBaseDiets`/`RefreshLargoDiets` |
| `Foods` | включение скрытой еды SR1 (`EnableHidden("LemonFruit", FoodKind.Fruit, "l.phase_lemon")`), добавление еды в дикие грядки/деревья (`AddToWild`) |
| `Spawns` | спавн в зонах с честной долей (взвешенная вставка в существующие спавнеры, без отдельных групп Cotton) |
| `Behaviours` | регистрация IL2CPP-типов (`Register<T>`), `Attach(def, go => …)`, `Attach<T>(def)` — на каждого заспавненного слайма |
| `Loc` | `AddLanguages(asm, csv)`, `String(key, table)` (кэш), `Static`, `Text`, `Has`, `NameLargo`, `ParseCsv` |
| `Mat` | `Find`, `Clone(имя, образцы…)`, `Copy`, `CloneByShader`; сеттеры-расширения `Col/Flt/Tx/Vec/Kw` (молча пропускают отсутствующие свойства), `Palette`, `TwinPalette`, `SetShaderKeepQueue`, цвета `Hex/Rgb/Mul/HDR/WithAlpha`, градиенты `Ramp/Tint`; живые материалы по имени, включая инстансы (`Live`), описание и дамп свойств (`Describe`, `Dump`), профиль подповерхностного рассеивания (`CopyDiffusionProfile`, `NeutralScatter`); хранитель материалов (`Track`) воскрешает уничтоженные игрой рантайм-материалы |
| `Faces` | клонирование лица с перекраской глаз/рта (`Clone`, `Recolor`), светящиеся глаза/рот на шейдере SR2 (`Glow`), невидимый рот (`Hidden`), защита своих материалов лица от уничтожения |
| `Looks` | структура внешности: `BodyMaterial`, `SetBodyMaterial`, `FaceIndex`, `InsertStructure`, `RemoveStructures`, `SkinnedObject`, `NewStructure`, `ClearRadiant`, `UnshareMaterials` |
| `PediaArt` | своя картинка в пэдии (`Register`) |
| `Fx`, `Snd` | эффекты игры по имени (`Play`, `Attach`, `Stop`), звуковые сигналы из своих WAV (`Snd.Cue`), стандартные (`Stock`), петли (`Loop`) |
| `World`, `Dmg` | игрок, время суток (`Hour`, `IsDay`), эмоции слайма, земля (`Grounded`, `GroundBelow`, `GroundPoint`, `GroundPointToward`; `GroundMask` без слоёв актёров), `Spawn`, `ActorsNear` (`ActorMask`: слайм 15, плорты 11 и слои, собранные с префабов в игре), `ActorLayerNames`, `AllOf`, `Nearest`, `Freeze`, `Despawn`, `RootBounds`, здоровье и радиация игрока (`PlayerStats`, `Rads`), телепорт между регионами (`Teleport`, `Locations`), мгновенный перенос актёра с эффектами и очисткой шлейфов (`Blink`), урон игроку с нужным источником (`Dmg.Player`) |
| `SlimeEvents` | события без Harmony: `OnEat(def, (слайм, еда) => …)`, `OnWater(def, слайм => …)`, `OnNaturalSpawn`; по желанию и для ларго; форма по компоненту — `OnWater<QuantumSlime>(q => …)`, `OnEat<T>((компонент, еда) => …)` |
| `Movement` | перехват движения слайма (`TakeOver(go, цель => …)`, `Release`): без прыжков SR2, к еде — своим способом (катится, бежит, телепортируется) |
| `SlimeActor` | обёртка над слаймом вместо повторяющихся `GetComponent` в каждом поведении: `Def`, `Rb`, `Emotions`, `Vac`; состояние пылесоса (`Held`, `Launched`, `Captive`, `Tornadoed`, `Free`, `PreventCapture`), эмоции (`Agitation`, `Hunger`, `Fear`, `Asleep`, `Emote`, `SetEmotion`), `IsLargo`, `Has`, `Scale`, `Center`, `Grounded`, `Speed`, `DistToPlayer` |
| `MaterialSwap` | временная подмена материалов слайма с возвратом оригинала (`Set`, `SetWhere`, `SetBody`, `Hide`, `Restore`, `RestoreAll`, `Active`): маскировка, голограмма, вспышка |
| `CorralWalls` | стены загонов SR2 (`Walls`, `Refresh`): пройти сквозь стену и снова стать твёрдым, не застряв в ней (`PassThrough`, `Solid`, `InsideWall`, `Update(слайм, сквозь)`, `IsPassing`) |
| `Aura` | облучение через `PlayerRadAbsorber` игры (`Irradiate`: радиус, рад/с, запасной урон, отметка для `RadHud`), урон по площади с интервалом (`Damage`), `PlayerInside` |
| `Gfx`, `Noise` | процедурные меши (`SphereMesh`, `Quad`, `Sphere`, `MeshObj`), билборды (`Billboard`, `BubbleBillboard`), искры и шлейфы на основе эмиттера SR2 (`Sparkles`, `Trail`), ленты (`Ribbon`), генерируемые текстуры с кэшем (`Generated`, `NoiseNormal`, `BubbleTex`, `PixelMask`, `TearMask`, `Dot`, `Square`, `Scanlines`), текстуры игры (`GameTex`), PNG (`FromPng`, `SavePng`, `ReadableCopy`), шум (`Noise.Value/Tile/Fbm`) |
| `MeshKit` | раздутые/гранёные копии меша (`Inflated`, `Facet`), дополнительный скиннированный слой поверх тела, который сжимается вместе со слаймом (`AddLayer`: стекло, оболочка, свечение), запекание 3D-узора в UV-развёртку без швов (`BakeSurface`), `BodyRenderer`, `RayTri` |
| `Voices` | свои реплики слайма: копия звуков с громкостью и микшером оригинала, меняются только клипы (`Apply`, `CueLike`) |
| `RadHud` | включает спрятанный в SR2 счётчик радиации из SR1 над полоской здоровья, по желанию — спад радиации как в SR1 (`Enable`, `NoteExposure`). Это тот же объект `RadMeter`, которым может управлять другой мод (например, RadHud в ClassicSlimes): два счётчика не появятся, но спад (`recovery > 0`) включайте только в одном месте — иначе радиация снимается дважды |
| `Diag` | текстовые дампы: иерархия объекта с компонентами (`Hierarchy`), определение слайма со структурами, мешами и материалами (`Slime`), запись в `UserData\CottonLibrary` (`Write`) |
| `ModAssets` | встроенные ресурсы мода: `Bytes`, `Text`, `Texture`, `Sprite`, `Clip` (16-bit WAV), `Mesh` (формат CSM1 из `ClassicSlimes\tools\export_assets.py`), `Bundle` |
| `Gc` | пауза GC на время генерации (`Window`), безопасная работа с IL2CPP-массивами (`Array`, `Set`, `Append`) |
| `Il2CppExt` | `Is<T>`, `As<T>` (по нативному классу), `Same`, `Alive`, `ToIl2CppArray`, `ToManagedList`, `PtrSet`, `PtrMap` |
| `Scheduler` | отложенные действия: `Frames`, `Seconds`, `Every` |
| `LiveConfig` | настройки MelonPreferences на лету: `Watch(entry, v => …, fireNow)` — обработчик при каждом изменении (из консоли, другого мода, после правки файла и `cfg reload`); `Get`/`Set` любой записи по имени, `ReloadFromDisk`, `Save` |
| `Compat` | другие моды без жёсткой ссылки: `IsLoaded`, `Find`, `Version`, `Mods`, `Multiplayer` (SR2MP), `SR2E` |
| `ForgeLog`, `Safe` | лог с префиксом, `WarnOnce`; `Safe.Run` — вызов с перехватом исключений |
| `DevConsole` | команды из файла (см. ниже), `Register(имя, справка, a => …)` для своих команд |

## Смешивание ларго

Cotton 0.3.1 собирает внешность ларго из копии PinkRock: тело и лицо — по флагам `Keep*`, цвета — смесь RGB, части
второго слайма просто дописываются. Отсюда грязные цвета, двойные уши и хвосты, лицо PinkRock у ларго, розово-каменные
radiant-варианты и перекрашенные родители. Форк собирает внешность движком `LargoBlend` (`Forge/Anatomy/LargoBlend.cs`) —
для **всех** ларго Cotton, в том числе модов на API 0.3.1. Имена и ID ларго не меняются, диета только дополняется
(«Паритет с ванильными ларго»).
Статус: проверено в игре (ClassicSlimes, 60 ларго).

| Что | Как выбирается |
|---|---|
| Шаблон | ванильное ларго `Pink<Родитель>`, если оно есть, затем по `TemplateOrder`, затем PinkRock. От шаблона — префаб и эффекты |
| Тело | у кого оно есть → рецепт пары → стили → флаги `Keep*Body` → «сила» тела (особые шейдеры Phosphor, Sloomber, Twin, Hyper, Boom выше обычного тела) |
| Лицо | если лицо рисует само тело — от владельца тела; иначе рецепт → стили → флаги `Keep*Face` → выразительность лица |
| Цвета | `Auto` при `LearnFromVanilla`: палитра одного родителя — того, чьи цвета чаще побеждают в ванильных ларго (ниже). Иначе смесь в OKLCh по дуге оттенков, серые цвета только подкрашивают: близкие оттенки — `Blend`, далёкие (> 40°) — `Split` (сверху доминирующий, снизу второй). Веса цвета: Gold 2, Saber и Shadow 1,6, Hyper 1,5, Phosphor 1,3, Flutter 0,6 |
| Эффекты тела | переносятся на тело ларго: трещины Boom, Hyper, Twin, Sloomber, свечение Phosphor |
| Части | сначала владельца тела, затем второго; без дублей по имени элемента и по слоту (уши, хвост, крылья, усы, антенны, очки, лоб, верх). Части оставляют 35 % своего цвета (`PartIdentity`); части цвета тела берут тело ларго |
| Эффекты внешности | блеск, торнадо, лоза, кристаллы, взрыв, каменная форма, рёв, смерть, шок, кубит — от родителя, у которого они есть |
| Radiant | свой вариант на каждого родителя с radiant-внешностью: части и палитра из неё плюс аура `RadiantAura`. Нет radiant у родителей — нет и у ларго |
| Ошибка | ларго собирается кодом 0.3.1, счётчик `Fallbacks` в `blend` |

Все материалы ларго — свои копии: родители не меняются. Копии называются по исходнику (`TabbyBody_largo`, у radiant —
`_largoR1`), поэтому моды, которые ищут части ларго по имени материала родителя, продолжают их находить.

### Паритет с ванильными ларго

Кроме внешности, ларго 0.3.1 отличаются от ванильных «начинкой»: на префабе остаются ссылки на PinkRock, компоненты
родителей копируются не полностью, ларго нет в части групп игры, в педии и вакпаке — иконка и full art PinkRock. После
`LargoBlend` форк догоняет ларго до ванильных (`Forge/Anatomy/LargoParity.cs`, настройка `LargoParity`). Правила не зашиты
в код, а выучены у игры: `VanillaSlimes` (`Forge/Content`) запоминает ванильные слаймы до загрузки модов и по ванильным
ларго считает, что у них общее.

| Шаг | Что делает |
|---|---|
| Ссылки | поля компонентов префаба, указывающие на шаблон (определение и его внешности), переводятся на ларго — в том числе элементы массивов и списков. То же — у клонов `SlimeBuilder` |
| Остатки шаблона | компоненты, которые шаблон получил от своего второго родителя (которого у ларго нет), удаляются — например, поведение Rock у ларго без Rock. Что чьё — выучено по ванильным ларго |
| Компоненты | недостающие компоненты родителей копируются с полями (и полями базовых классов игры); ссылки внутри префаба родителя — на тот же объект ларго (путь, тип, номер). Не копируются `AweTowardsLargos`, компоненты глаз и рта, `ColliderTotemLinkerHelper` (`SkipComponents`) и инъекции модов |
| Группы | ларго — в группы, где состоят ≥ 90 % ванильных ларго, и в группы ванильных ларго своих родителей; базы `SlimeBuilder` — в группы ≥ 90 % ванильных баз (группы «только для ларгуемых» — если слайм может быть ларго). Исключить группу — `LargoParity.SkipGroups` |
| Педия | full art шаблона убирается, иконка — своя (ниже) |
| Зоны | объединение зон родителей, если так у ≥ 80 % ванильных ларго |
| Диета | любимая продукция — как у большинства ванильных ларго; пустые поля (tarr, группы плортов, нестабильный плорт) — от шаблона, затем `RefreshEatmap` |
| Плорты | плорты модовых родителей входят в группы, где состоят все ванильные плорты (`EdiblePlortFoodGroup` и др.), и в группу плортов, которую ест второй родитель. Cotton их не добавлял: плорты Rad никто не ел и ларго не появлялись |
| Анимации | переопределения анимаций (покой, взмахи крыльев, укус) — от родителя-владельца тела, иначе от второго, а не от шаблона |
| `AppearancesDynamic` | внешности шаблона заменяются внешностями ларго, чужие убираются, дубли тоже |

Что выучено у ванильных ларго (`LargoBlend.LearnFromVanilla`, `largoaudit learn`):

- **Тело и лицо**: если ванильные ларго однозначно показывают, чьё тело или лицо побеждает (`VanillaSlimes.BodyShare`/`FaceShare`),
  это решает после рецептов, стилей и флагов, но до «силы». Если доля известна у обоих родителей, решает разница ≥ 0,2.
- **Цвета**: у 149 из 171 ванильного ларго палитра — ровно одного родителя. Для каждого слайма считается, как часто его
  палитра побеждает (Hyper 14/14, Pink 14/14, Sloomber 13/13 … Twin 0/13); у модового слайма доля — из веса цвета стиля
  (вес 1 = 0,5, 1,4 = 0,7). Побеждает большая доля, при равенстве — владелец тела. Radiant-вариант берёт radiant-палитру
  родителя, чьи цвета у ларго. Смешивание остаётся для рецептов с балансом, `Colors(LargoColorMode.Blend)` и
  `LargoBlend.DefaultColorMode`.
- **Части**: ларго-версии частей (крылья Flutter, облако Sloomber и его частицы), какие части второго родителя пропадают
  рядом с частями владельца (плавники Angler рядом с ушами, хвост Cotton рядом с хвостом, уши Ringtail рядом с ушами) и
  кто побеждает в одном слоте (уши Cotton 4/4, уши Batty 3/4, хвост Ringtail 3/3). Сколько своего цвета оставляют части —
  `PartIdentity` (ванильные части почти всегда берут цвет тела, поэтому выученное значение не используется).

**Иконки** (`Forge/Visual/SlimeIcons.cs`, настройка `LargoIcons`). Если шаблон — ванильное ларго одного из родителей,
берётся его иконка, иначе — иконка родителя-владельца тела. Она перекрашивается в палитру ларго (OKLCh, по оттенкам); если
ларго носит палитру второго родителя — в цвета иконки этого родителя: пиксели переносятся по её шкале светлоты (`Ramp`,
`RampBands` = 5), потому что иконки освещены и используют цвета палитры по-разному (по цветам палитры ларго Rad + Pink
выходило бордовым). В правый нижний угол ставится маленькая иконка второго родителя (кроме Pink;
`Badges`, `BadgeScale`). У radiant-вариантов —
свои иконки. Собираются в фоне, по 2 за кадр (`PerFrame`), размер `Size` = 256. Иконку, которую поставил мод, движок не
трогает; своя иконка пары — `LargoRecipe.For(a, b).Icon(sprite)`.

**Аудит** (`Forge/Dev/LargoAudit.cs`). Через 10 с после генерации все ларго Cotton сверяются с ванильными: регистрация
(списки, поиск по родителям, дубли `referenceId` и хэшей), группы, диета (плорты обоих родителей, еда, превращения, tarr,
плорты модовых родителей в группах ванильных плортов), внешность, иконка, префаб (компоненты, ссылки на шаблон и чужие
префабы). Аудит ждёт готовых иконок (до 30 с). Итог — строка `[largo audit] N generated largos, M with
problems` в логе и файл `UserData\CottonLibrary\largo_audit.txt`; выключить — `LargoAudit.AutoRun = false`. Команда
`largoaudit`: `<ларго>` — одно ларго, `all` — все, `vanilla` — самопроверка (план `Preview` для каждого ванильного ларго
сравнивается с настоящим: тело, лицо, палитра, части → `largo_selftest.txt`), `learn` — что выучено → `largo_vanilla.txt`.

Проверено в игре (ClassicSlimes, 60 ларго): `[largo audit] 60 generated largos, 0 with problems`, у всех своя иконка.
Самопроверка `largoaudit vanilla`: тело 42/46, лицо 92/104, палитра dE 0,025 (лучшее возможное 0,012), совпадение частей
97 %. Это проверка на тех же ларго, по которым правила выучены, — оценка сверху.

Ограничения: иконки — перекраска готовых, а не рендер модели; ларго-версия тела Saber не выучена (мало примеров).

### API ларго

**Стиль** — правила слайма для всех его ларго. Объявлять до генерации ларго (`DeclareContent` или `SaveDirectorLoaded`):

```csharp
LargoStyle.Of("Sunny")
    .Body(LargoPriority.High)          // тело Sunny важнее тела второго родителя; Low — наоборот, Never/Always — жёстко
    .Face(LargoPriority.Low)           // лицо — от второго
    .Colors(1.5f)                      // вес цвета в смеси (1 — обычный)
    .Colors(LargoColorMode.Dominant)   // или режим: Auto, Blend, Split, Dominant, First (этот слайм), Second
    .PartColors(0.6f)                  // сколько своего цвета оставлять частям, 0..1
    .KeepOwnColors("SunnyRays")        // эти части не перекрашивать ("*" — все)
    .Drop("SunnyHalo")                 // эту часть в ларго не переносить
    .Template("PinkHoney")             // шаблон: префаб и эффекты
    .OnBuild(b => b.BodyMaterial.Flt("_GlossPower", 0.8f))
    .OnCreated((largo, other) => ForgeLog.Msg(largo.name + " + " + other.name));
```

В `SlimeBuilder`: `.Largo(s => s.Body().Colors(1.5f))`, после объявления — `LargoLook` (тот же `LargoStyle`).

**Рецепт** — одна пара (порядок слаймов не важен), сильнее стилей:

```csharp
LargoRecipe.For("Sunny", "Phosphor").Body("Phosphor").Face("Sunny").Colors(LargoColorMode.Split);
LargoRecipe.For("Sunny", "Tabby").Balance("Sunny", 0.7f);                 // 70 % цвета от Sunny
LargoRecipe.For("Sunny", "Rock").ColorsOf("Rock").Template("PinkRock");
LargoRecipe.For("Sunny", "Boom").Palette(Mat.Hex("#FFE45C"), Mat.Hex("#FF8A00"), Mat.Hex("#6A2A00"));   // режим Custom
LargoRecipe.For("Sunny", "Gold").Icon(sunnyGoldIcon);                    // своя иконка вместо собранной
```

**Событие** — для любых ларго, в том числе чужих:

```csharp
LargoBlend.Building += b =>            // каждый вариант (обычный и radiant) перед сохранением
{
    if (!b.Has("Sunny")) return;
    if (b.IsRadiant) b.BodyMaterial.Flt("_GlossPower", 1f);
    b.Structures.RemoveAll(s => s.Element.Name == "SunnyHalo" && b.BodyFrom.name != "Sunny");
};
LargoBlend.Created += largo => { };    // ларго готово (после копирования компонентов)
```

`LargoBuild` (в `OnBuild` и `Building`): `Largo`, `First`/`Second` и их внешности, `Appearance`, `Template`, `Settings`,
`IsRadiant`/`RadiantSide`, `BodyFrom`/`FaceFrom`, `ColorMode`, `Balance` (доля второго), `DominantSide`, палитры `Palette`,
`FirstPalette`, `SecondPalette`, `BodyMaterial`, изменяемый список `Structures`, `Structure("Ears")`, `Other(родитель)`,
`Has(имя)`, `Recolored(материал, доля своего цвета)` — копия в палитре ларго, `Reason` и `Describe()` — почему так решено.

Флаги `LargoSettings`: новые `AutoBody` (0x400), `AutoFace` (0x800), `AutoColors` (0x1000), `Auto` — все три. С ними
движок решает сам, `Keep*` и `Merge*` не учитываются; без них флаги 0.3.1 — подсказка, стили и рецепты сильнее.
`SlimeBuilder` ставит `Auto` по умолчанию. Cotton 0.3.1 проверяет флаги через `HasFlag`, лишние биты ему не мешают: мод на
API 0.3.1 может передавать `(LargoSettings)0x1C00` всегда, а стили объявлять только на форке:

```csharp
if (Type.GetType("CottonLibrary.Forge.LargoStyle, CottonLibrary") != null) Styles();

[MethodImpl(MethodImplOptions.NoInlining)]     // типы форка JIT видит, только если метод вызван
static void Styles() => LargoStyle.Of("Rad").Body(LargoPriority.High);
```

Так сделано в ClassicSlimes (`src/LargoLook.cs`).

Остальное: `LargoBlend` — `DefaultColorMode`, `PartIdentity`, `AutoSplitHue`, `DominantHint`, `RadiantWeight`,
`ColorWeights`, `UniqueSlots`, `TemplateOrder`; `InfoOf(ларго)` и `All` — как собрано, `Built`/`RadiantVariants`/`Fallbacks`,
`Preview(a, b, флаги, structures)` — план без создания ларго (`structures: true` — и части), `LearnFromVanilla`,
`PickTemplate`, `BodyOf`, `PaletteOf`, `MergeStructures(a, b, флаги)`. `LargoParity` — `ResultOf(ларго)`, `Repoint`,
`MergeComponents`, `JoinLargoGroups`, `JoinBaseGroups`, `SkipGroups`, `SkipComponents`. `VanillaSlimes` — `IsVanilla`,
`Largos`, `Bases`, `LargoGroups`, `BaseGroups`, `ParentGroups`, `CoreComponents`, `ParentComponents`, `LargosOf`,
`BodyShare`/`FaceShare`, `Relearn`, `Describe`. `SlimeIcons` — `HowOf(ларго)`, `IsMade`, `Made`/`Failed`/`Pending`.
`LargoAudit` — `Check(ларго)`, `All`, `SelfTest`.
`LargoPalette` — `Of(внешность)`, `Mix`, `ApplyTo`. `ColorMix` — OKLab/OKLCh: `Mix`, `MixLab`, `Lighten`, `Saturate`,
`HueDistance`, `IsGrey`, `Hex`.

## Движок Forge

Поверх API контента — движок из модулей с чистыми интерфейсами (`Forge/Architecture/Contracts.cs`) и базовыми
реализациями. Подробно — [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Статус: собрано, в игре проверено частично (там же).

| Подсистема | Классы | Что даёт |
|---|---|---|
| Каркас | `ForgeModules`, `ActorRuntime`, `ForgeSave` | модули с `Init/Tick` и отключением по id, логика на объектах без IL2CPP-инъекции (`IActorLogic`), данные модов в сохранении |
| Ларго и анатомия | `AnatomySpec`, `Anatomy`, `LargoEngine`, `Archetypes`, `DietTools` | слои, части, голоса и поведения слайма, которые ларго наследуют и смешивают; диеты баз объединяются; архетипы SR1 `quantum`, `dervish`, `tangle`, `mosaic`, `glitch`, `rad`, `quicksilver` и SR2 `ringtail`, `batty`, `flutter`, `yolky` |
| Погода | `ForgeWeather` | активная погода (`WeatherKind`: дождь, гроза, пыльца, ветер…), ветер, молнии, реакции слаймов под открытым небом, свет/тень |
| Гаджеты и дроны | `GadgetKit`, `GadgetLogic`, `ExtractorLogic`, `PlortCollectorLogic`, `TeleporterLogic`, `DroneStationLogic`, `HelperDrone`, `DronePrograms` | клоны гаджетов, логика на поставленных гаджетах, экстракторы с догоном времени, телепорты по каналам, дроны-помощники в духе SR1 |
| Рынок | `PlortMarket`, `PriceModels` | регистрация плортов, модели цены (волатильность, тренд, перенасыщение, дефицит, погода, SR1), бумы и обвалы, память продаж |
| Визуал и звук | `MaterialPresets`, `MaterialAnimator`, `SoundBank`, `VoicePack` | пресеты `jelly`, `glass`, `prismatic`, `neon`, `metal`, `glitch`, `stone`; анимация материалов; банк звуков и голосовые пакеты с автобалансом громкости |
| Пылесос и HUD | `VacPack`, `FireModes`, `VirtualAmmo`, `ForgeHud`, `HudBar`, `HudCounter`, `HudText` | слоты и выстрелы, альтернативные режимы огня (клавиша `FireModeKey`), виртуальные слоты, модульные индикаторы HUD и всплывающие сообщения |

## Настройки

`UserData\MelonPreferences.cfg`, категория `[CottonLibrary]`: `SafeGc`, `FixDuplicateSlimes`, `FixLargoAI`,
`LocalizationInterval` (сек, 2), `CallAutoLargosLoaded`, `DevConsole` (false), `Verbose` (false),
`DisabledModules` ("" — id модулей движка через запятую), `FireModeKey` ("B"), `SmartLargos` (true — движок
смешивания ларго, false — код 0.3.1), `LargoRadiants` (true — radiant-варианты ларго),
`LargoParity` (true — паритет ларго с ванильными: ссылки, компоненты, группы, зоны, диета), `LargoIcons` (true — свои
иконки ларго).

## Dev-консоль

При `DevConsole = true` раз в 0,5 с читается `UserData\CottonLibrary\cmd.txt` (по команде в строке), ответ пишется в `out.txt`.
Команды: `help [команда]`, `info`, `slimes`, `spawn <тип> [кол-во] [дист]`, `kill [радиус]`, `time <час>`, `census`, `diet <слайм>`,
`largos <слайм>`, `mats`, `revive`, `fx <имя>|list`, `loc [reload]`, `dmgsources`, `report`.

Инструменты для отладки внешности и тестов:
- мир: `locs [фильтр]`, `goto <локация|регион> [x y z]`, `hp`, `rad <кол-во>`, `radhud [x y] [спад/с]`;
- слаймы (ближайший нужного типа): `pose`, `lineup <тип,тип,…>`, `tree`, `hier`, `track`, `wobble`, `rinfo`,
  `layer <тип> <материал|off>`, `dump <слайм>`, `palette <слайм>`;
- материалы на лету (имя, `*` — префикс): `props`, `flt`, `col`, `vec`, `kw`, `rq`, `texst`,
  `tex <мат> <свойство> <текстура игры|файл.png>`, `texdump`;
- скриншоты: `vac` / `vac on` — спрятать и вернуть пылесос;
- `gallery <фильтр|мат,мат,…> [макс]` / `gallery clear` — шары с материалами перед камерой для сравнения;
- `cfg [категория] [запись] [значение]` — посмотреть и поменять любую настройку MelonPreferences на лету, `cfg reload`;
- `walls [refresh]` — стены загонов и слаймы, проходящие сквозь них; `mods` — загруженные моды, мультиплеер.
- движок: `modules`, `arch [list | <id> [сила] | off]`, `anatomy <слайм>`, `weather`, `market …`, `drone sell|gather|list|clear`,
  `firemode [id|builtins]`, `ammo`, `hud [test|clear]`, `preset <id> [r g b]`, `save` (подробно — в docs/ARCHITECTURE.md);
- ларго: `blend` — настройки, стили, рецепты, счётчики; `blend <ларго>` — шаблон, тело, лицо, режим, палитра, radiant и
  причины; `blend <a> <b>` — то же и план с флагами `Auto`; `blends` — все собранные ларго;
  `largoaudit [<ларго>|all|vanilla|learn]` — проверка ларго против ванильных (файлы в `UserData\CottonLibrary`).

Свои команды — `DevConsole.Register(...)`.

## Перенос ClassicSlimes

1. Заменить `CottonLibrary.dll` форком: ClassicSlimes продолжит работать как есть, его собственные фиксы просто станут лишними.
2. Затем по желанию перейти на Forge: наследовать мод от `ForgeMod<M>`, слаймов описать через `Slime(...)` и удалить свои
   `SlimeIndexDedup`, `SloomberPaletteGuard`, спавн, регистрацию поведений, `PediaArt`, паузу GC и патч цикла локализации.
3. Общие части ClassicSlimes уже есть в форке и могут уйти из мода: `Gfx` (билборды, искры, ленты, текстуры), `Swirl` →
   `MeshKit.BakeSurface`, оболочки из `Experiments` → `MeshKit.AddLayer`, патчи воды/еды/прыжков из `Hooks` →
   `SlimeEvents` и `Movement`, `RadHud`, голоса → `Voices`, светящиеся лица и пустой рот → `Faces.Glow`/`Faces.Hidden`,
   общие команды `DevCmd` и дампы `Diag` → встроенная dev-консоль.
   Из `Behaviours`: поля `_rb/_vac/_emo` и проверки «в пылесосе/брошен/спит» → `SlimeActor`, классы `Swap` (маскировка
   Glitch, фаза Quantum, вспышки Quicksilver) → `MaterialSwap`, проход Quantum сквозь стены загона → `CorralWalls`,
   облучение RadSlime → `Aura.Irradiate`, телепорт → `World.Blink`; реакции на `Cfg` без перезапуска → `LiveConfig.Watch`
   и команда `cfg`; проверка SR2MP → `Compat.Multiplayer`.
4. Проверить в игре старое сохранение: ссылки на слаймов и плорты не меняются.

## Авторы и лицензия

Cotton Library — PinkTarr (gitlab.com/thean-mcgarity/CottonLibrary). CottonForge (форк, исправления и слой Forge) — slitvin12.
В upstream-репозитории нет файла лицензии; перед публичным распространением форка лучше согласовать это с автором.
