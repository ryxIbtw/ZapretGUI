# Zapret GUI

Минималистичный интерфейс для [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) от **Flowseal**.
GUI by **ryxIbtw**.

[English below](#english)

## Установка

1. Скачайте и распакуйте [релиз zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube/releases/latest), например в `C:\zapret`.
2. Положите `ZapretGUI.exe` в эту папку, рядом с `service.bat`.
3. Запустите. Windows попросит права администратора: они нужны драйверу WinDivert.

## Возможности

**Главный экран**
- Большая кнопка включает и выключает обход. `winws.exe` работает в фоне, консольных окон нет.
- Индикаторы **YouTube ● Discord ●** раз в минуту проверяют, открываются ли сайты: зелёный — работает, жёлтый — частично, красный — нет. Клик по индикатору проверяет сразу.
- **Стратегия**: стрелки ‹ › или колесо мыши, клик по названию открывает список. Если обход включён, он сразу перезапускается с новой стратегией.
- **Подобрать стратегию** по очереди включает каждую стратегию, проверяет YouTube и Discord и оставляет первую рабочую. Отменить можно кнопкой питания.
- **Автозапуск обхода с Windows** (служба), **игровой фильтр** с портами, **IPSet**.
- **Ещё**: свои списки доменов и исключений, диагностика, классическое меню `service.bat`.

**Настройки** (шестерёнка)
- Язык: русский или английский. Тема: тёмная (зелёный акцент) или светлая (синий).
- Запуск программы с Windows сразу в трей, через планировщик, без запроса UAC.
- Крестик сворачивает в трей или закрывает программу.
- **Сторож** перезапускает обход, если `winws.exe` упал (не больше 3 раз за 5 минут).
- **Диагностика** проверяет BFE, прокси, VPN, AdGuard, Killer, Check Point, SmartByte, DNS, путь установки, драйвер, hosts и другие программы обхода. Найденные проблемы исправляются кнопкой, кэш Discord очищается в один клик.
- **Fake-файлы** для Discord UDP и игрового фильтра.
- **Обновление zapret в один клик**: программа скачивает релиз и заменяет файлы. Ваши списки, IPSet, игровой фильтр и fake-файлы сохраняются. Стратегии, которых нет в новой версии, не удаляются, а переносятся в папку `old-strategies\v<старая версия>`.

Настройки хранятся в тех же файлах, что использует `service.bat`, поэтому GUI и консольное меню можно использовать вперемешку.

## Сборка

Нужна только Windows: используется встроенный компилятор .NET Framework 4.x.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Лицензия

Код интерфейса распространяется под лицензией [MIT](LICENSE). Это неофициальный интерфейс: сам zapret-discord-youtube принадлежит его авторам и распространяется под собственной лицензией MIT, в этот репозиторий он не входит.

---

## English

A minimal GUI for [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) by **Flowseal**. GUI by **ryxIbtw**.

Put `ZapretGUI.exe` next to `service.bat` in your zapret folder and run it (administrator rights are required by WinDivert).

Features: one-button on/off, live YouTube/Discord availability indicators, automatic strategy finder, Windows autostart (service), game filter, IPSet, watchdog, built-in diagnostics with one-click fixes, fake file switcher, one-click zapret update that keeps your lists and settings, tray mode, dark/light theme, Russian/English UI.

Build: `powershell -ExecutionPolicy Bypass -File build.ps1`

License: [MIT](LICENSE). This is an unofficial GUI; zapret-discord-youtube itself belongs to its authors and is not included in this repository.
