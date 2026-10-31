# Electron probe

用于验证 PrivacyGuard 的 `TZ` / `LANG` / `--lang` 对 Electron 主进程与渲染进程的实际影响。

```powershell
npm install
$env:PG_OUT="$PWD\out.json"
$env:TZ='UTC'
$env:LANG='en_US.UTF-8'
$env:LC_ALL='en_US.UTF-8'
.\node_modules\electron\dist\electron.exe . --no-sandbox --lang=en-US
Get-Content .\out.json
```

输出包含：

- `main.mainIntlTZ` / `main.mainIntlLocale` / `main.appLocale`
- `main.preferredLanguages` / `main.appLocaleCountry`
- `renderer.navigatorLanguage` / `renderer.navigatorLanguages`
- `renderer.intlTZ` / `renderer.intlLocale` / `renderer.date`

不要把 `node_modules` 或 `out*.json` 提交到仓库。
