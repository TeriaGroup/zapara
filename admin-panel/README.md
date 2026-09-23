# Панель оператора

Локальное приложение Laravel / Filament рядом с «Расписание военмех».
Оно ходит в те же схемы PostgreSQL, включая `operator`. В git этот каталог не входит, в compose сервера его нет.

Zapara.Server страницы `/Admin` не отдаёт: эти пути отвечают 404.
Исходники Razor лежат в `src/Zapara.Server/Pages/Admin` и по HTTP не открываются.

Секреты, `.env`, `vendor` и локальную sqlite в репозиторий не класть.
Картина продукта — `docs/STATUS.md`.
