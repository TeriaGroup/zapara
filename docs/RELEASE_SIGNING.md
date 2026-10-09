# Проверка обновлений Windows

Автообновление Windows скачивает архив из релиза и перед распаковкой проверяет его:

1. В релизе должен быть файл `SHA256SUMS`. Первая строка `version: 2.1.43` указывает версию релиза, дальше
   идут строки в формате `sha256sum`: `<sha256>  <имя файла>`. Если файла нет (404), архива нет в списке,
   хеш не совпадает, версия в файле не равна версии релиза или ниже установленной, архив удаляется и
   установка отменяется. Если файл не удалось получить из-за сети или ошибки сервера, архив остаётся на
   диске и проверяется при следующей попытке.
2. Если в `UpdateVerifier.ReleasePublicKeyPem` (`src/Vograph.Core/Services/UpdateVerifier.cs`) задан открытый
   ключ, обязателен ещё `SHA256SUMS.sig`: подпись ECDSA P-256 / SHA-256 файла `SHA256SUMS` целиком, вместе
   со строкой `version:`. Без валидной подписи или без строки `version:` установка тоже отменяется. Пока ключ
   пустой, проверяется только `SHA256SUMS`, а файл без строки `version:` принимается.

Перед запуском установщика хеш архива на диске проверяется ещё раз.

Клиенты 2.1.42 и старше ничего не проверяют. Проверка работает начиная с версии, в которую вошло это изменение.
После её выхода каждый Windows-релиз обязан содержать `SHA256SUMS`, иначе новые клиенты не обновятся
автоматически (страница релиза в браузере по-прежнему работает).

## Ключ подписи (один раз)

Ключ создаёт и хранит владелец релизов, вне репозитория и вне облачных папок. Подойдёт любой из вариантов.

OpenSSL:

```sh
openssl ecparam -name prime256v1 -genkey -noout -out zapara-release.key
openssl ec -in zapara-release.key -pubout -out zapara-release.pub.pem
```

PowerShell 7:

```powershell
$k = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
Set-Content zapara-release.key $k.ExportPkcs8PrivateKeyPem() -NoNewline
Set-Content zapara-release.pub.pem $k.ExportSubjectPublicKeyInfoPem() -NoNewline
```

Дальше:

1. Содержимое `zapara-release.pub.pem` вставить в `ReleasePublicKeyPem` целиком, вместе со строками
   `-----BEGIN PUBLIC KEY-----` и `-----END PUBLIC KEY-----`, и выпустить версию с этим ключом.
2. С этого релиза каждый Windows-релиз подписывать (см. ниже). Клиент с ключом не примет неподписанный релиз.
3. Закрытый ключ `zapara-release.key` нельзя коммитить и публиковать. Резервную копию держать офлайн.
   Если ключ утерян, придётся выпустить версию с новым открытым ключом, и пользователи обновят её вручную.

## Выпуск релиза

```powershell
pwsh scripts/release/sign-release.ps1 -Version 2.1.43 `
  -Assets out/ZAPARA_win-x64.zip, out/ZAPARA_android-debug.apk `
  -PrivateKey D:\keys\zapara-release.key -PublicKey D:\keys\zapara-release.pub.pem
```

`-Version` должен совпадать с тегом релиза (`v2.1.43` → `2.1.43`). Скрипт пишет рядом с первым файлом `SHA256SUMS` и `SHA256SUMS.sig`. Их нужно загрузить в релиз вместе
с ассетами, имена менять нельзя. Без `-PrivateKey` создаётся только `SHA256SUMS`: так можно работать,
пока ключ не заведён. Подписывать заново нужно после любой замены ассета в релизе.

Ручная проверка через OpenSSL:

```sh
grep -v '^version:' SHA256SUMS | sha256sum -c -
openssl dgst -sha256 -verify zapara-release.pub.pem -signature SHA256SUMS.sig SHA256SUMS
```
