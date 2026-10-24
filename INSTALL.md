# Установка / Installation

Экспериментальный plugin-only пакет 0.3.0-preview.1 для Victory At Sea Pacific 1.14.2, Windows x64, Unity Mono/.NET 3.5. Объём проверок: [отчёт промышленности](docs/validation-industry.md). `releaseReady` остаётся `false`.

## Сначала зависимость

1. В Steam выберите игру → Управление → Просмотреть локальные файлы. Это ваш `GamePath` (папка с `VictoryAtSeaPacific.exe`).
2. Полностью выйдите из игры, включая тестовые клиенты.
3. Установите **BepInEx 5.4.23.5 x64** из официального релиза: https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 . Используйте `BepInEx_win_x64_5.4.23.5.zip`, не IL2CPP/BepInEx 6. Сначала проверьте, что другой мод не установил собственный `winhttp.dll`/загрузчик: не перезаписывайте его вслепую. При конфликте согласуйте загрузчик с автором другого мода. Запустите/закройте игру один раз и проверьте `BepInEx/LogOutput.log`.
4. Community Mods не поставляет и не заменяет загрузчик, Harmony, игровые библиотеки или настройки.

## Установка и обновление

Распакуйте ZIP **в отдельную папку**, не поверх папки игры. В PowerShell перейдите в эту папку, затем:

```powershell
$game = Read-Host 'Полный путь папки игры'
.\scripts\Manage.ps1 -Action Install -GamePath $game -AllowExperimental
```

Для обновления используйте тот же вызов из новой распакованной версии. Путь задаётся явно, автоматического выбора неоднозначной Steam-библиотеки нет. Если PowerShell блокирует скачанные сценарии, сначала прочитайте их и разблокируйте проверенные файлы средствами Windows; глобально политику безопасности менять не требуется.

Установщик проверяет SHA-256 и размер оригинальной `Assembly-CSharp.dll`, наличие BepInEx 5.4.23.5, SHA-256 четырёх DLL пакета, отсутствие запущенного процесса из целевой папки, отсутствие junction/symlink и владение прежними файлами. Дополнительная копия известной сборки вне её штатного пути блокирует операцию, даже если файл переименован. Неопознанные файлы даже с совпадающим содержимым не перезаписываются. Изменённая собственная DLL также блокирует операцию. Сначала выясните причину, не удаляйте чужие файлы ради обхода проверки.

### Миграция старого набора

Для установленного проверенного набора Hotfix 1.14.2.1 + Patrol 0.1.0 + Interface **0.1.2** добавьте `-MigrateBaseline`. Принимаются только все три точных исходных SHA-256 (список в `scripts/Common.ps1`) и их прежние пути. Другие версии не принимаются. Старые `.vasp-hotfix-transaction.json`/legacy manifests остаются нетронутыми и **не используются** для принятия владения загрузчиком/конфигом. После миграции не запускайте старый rollback поверх новой установки: он не знает новый формат. Откат миграции выполняется новым `Rollback`, который возвращает три исходные DLL и оставляет legacy records действительными.

Настройки в `BepInEx/config` установщик никогда не трогает. Их перенос/применение выполняет сам плагин. Активные задания патруля в сохранения не добавляются.

### После запуска

Откройте «Модификации». В правой секции Community Mods используйте колесо или полосу прокрутки; внизу показано фактическое состояние. H1–H5 требуют перезапуска, остальные переключатели действуют сразу. Баланс строительства включён по умолчанию: ИИ на обычной сложности строит со скоростью 125% игрока. Отключение возвращает 150%. Процент можно задать в диапазоне 100–150 в `BepInEx/config/com.vasp.communitymods.cfg`; после ручного редактирования cfg перезапустите игру.

Промышленность включена по умолчанию, а все порты начинают без платных улучшений. На «Мостике» выберите собственный домашний порт: над его карточкой появится покупка производства. Первый уровень стоит 3 000, второй — ещё 5 000 облигаций. Покупку нужно подтвердить. Сохраните кампанию штатным способом, чтобы сохранить купленные уровни; [подробности](docs/industry.md). После отката на версию без промышленности производство вернётся к базовому. Пересохранение кампании без этого модуля удаляет его дополнительные уровни из нового файла, поэтому сохраняйте исходную копию.

## Откат и удаление

```powershell
.\scripts\Manage.ps1 -Action Rollback -GamePath $game
.\scripts\Manage.ps1 -Action Remove -GamePath $game
```

Rollback возвращает состояние перед последней успешной установкой/обновлением. Remove удаляет только четыре проверенных по хешам файла, записанных в `.vasp-community-owned.json`; конфиги, сторонние плагины, loader, пустые каталоги и резервные копии сохраняются. Не используйте ручное удаление всей `BepInEx`.

Перед изменениями создаются `.vasp-community-backups/<id>/snapshot.json` и копии прежних собственных DLL. При обычном исключении выполняется автоматический откат. При внешнем изменении файла, запуске игры во время операции или аварийном завершении процесса сценарий останавливается безопасно и оставляет `.vasp-community-journal.json` и резервную копию: сохраните их и обратитесь за помощью. Автоматическое восстановление после отключения питания не заявляется. Не редактируйте и не удаляйте журнал, пока не проверены все before/after SHA; не перезаписывайте чужие изменения. Операции требуют права записи в целевую папку; при ошибке доступа не запускайте игру во время восстановления.

## Сборка для разработчика

Windows PowerShell 5.1+, установленная поддерживаемая игра и BepInEx. По умолчанию используется Framework `csc.exe` C# 5 (Framework 4.x compiler, но **не Framework 4.x target**): `/noconfig /nostdlib` и явные CLR2/.NET3.5 Unity reference assemblies из локальной игры. Возможен `-CompilerPath` к совместимому `csc.exe`. Никаких сетевых restore и личных путей в проектах.

```powershell
.\build.ps1 -GamePath $game
.\scripts\Package.ps1 -Version '0.3.0-preview.1'
.\tests\packaging.ps1 -GamePath $game
```

Сборка берёт только `src/Hotfix/*.cs`, `src/Patrol/*.cs`, `src/Interface/*.cs` + embedded `RuOverrides.tsv`, `src/Bridge/*.cs`. Имена: `VaspHotfix.dll`, `VaspScoutPatrol.dll`, `VaspInterfaceTools.dll`, `VaspCommunityMods.dll`. Выход `artifacts/plugins`: только четыре собственных DLL; ссылки приватные (эквивалент `CopyLocal=false`), библиотеки игры не копируются. Пакет собирается по строгому allowlist, без reports, probes, saves и библиотек. Рядом с ZIP создаётся SHA-256. Публичный репозиторий и CI не должны включать локальные игровые ссылки; полноценная сборка требует легальной локальной игры.

### Проверка в настоящем Unity/Mono

`tests/runtime.ps1` предназначен для разработчика. Подготовьте отдельный приватный клиент той же игры вне установленной папки, со своим BepInEx/config и только четырьмя плагинами набора. EXE и ресурсы должны соответствовать оригиналу. Сценарий проверяет EXE, отклоняет другие активные helper-плагины, копирует результаты сборки и компилирует отдельный helper. Он загружает копию явно указанного сохранения, перехватывает штатные методы записи/удаления сохранений и завершает свой клиент после проверки. Сторонние плагины в этот клиент добавлять нельзя.

```powershell
.\tests\runtime.ps1 -GamePath $game -ClientPath 'D:\PrivateVaspClient' -ReportPath 'D:\PrivateVaspReports\main' -InputSave 'D:\SaveCopies\campaign.data'
# Дождитесь ALL PASS main в runtime.txt и завершения выданного PID.
.\tests\runtime.ps1 -GamePath $game -ClientPath 'D:\PrivateVaspClient' -ReportPath 'D:\PrivateVaspReports\restart' -Phase restart
```

Сценарий возвращает PID сразу. Проверьте `runtime.txt`, `unity.log`, два PNG меню и `tested-build.json`, затем отключите `VaspRuntimeValidation.dll` в приватном клиенте, переименовав расширение в `.dll.disabled`. Helper нельзя устанавливать в игровую папку или публичный ZIP. Только `ALL PASS` и совпадение SHA-256 с упакованными DLL связывают проверку с релизом; повторная сборка требует повторной проверки.

Основной тест также создаёт только в каталоге отчёта `industry-roundtrip.data`, `expected-ports.txt` и `industry-state.bin`. Для проверки после полного перезапуска скопируйте два последних файла в отдельный каталог отчёта и вызовите `runtime.ps1 -Phase industry-restart -InputSave <industry-roundtrip.data>`. Для проверки загрузки без мода вызовите `tests/vanilla-industry.ps1` с теми же GamePath/ClientPath и отдельным ReportPath, передав этот приватный файл как InputSave. Сценарий временно отключает четыре DLL только в приватном клиенте, запускает независимый helper и возвращает прежние файлы после завершения. Оригинальный установленный клиент не изменяется.

## English quick start

Requires VASP 1.14.2 and separately installed BepInEx **5.4.23.5 x64**. Close the game. Extract the plugin ZIP outside the game folder. Run `scripts/Manage.ps1 -Action Install -GamePath <your game folder> -AllowExperimental`. Use the same command to update; `-Action Rollback` reverts the last update; `-Action Remove` removes owned, checksum-verified plugins only. Loader, config and third-party files are preserved. Exact three-DLL baseline migration requires `-MigrateBaseline`. Never run the old installer/rollback after migration. If a transaction journal remains, preserve backups and request assistance rather than deleting unknown files. CLR/build and fixture tests are not Unity gameplay verification.
