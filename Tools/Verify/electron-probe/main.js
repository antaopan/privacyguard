const { app, BrowserWindow } = require('electron');
const fs = require('fs');

function mainSnapshot() {
  const dt = Intl.DateTimeFormat().resolvedOptions();
  return {
    envTZ: process.env.TZ || null,
    envLANG: process.env.LANG || null,
    envLANGUAGE: process.env.LANGUAGE || null,
    mainIntlLocale: dt.locale,
    mainIntlTZ: dt.timeZone,
    appLocale: app.getLocale(),
    appLocaleCountry: app.getLocaleCountryCode(),
    preferredLanguages: app.getPreferredSystemLanguages(),
    langSwitch: app.commandLine.getSwitchValue('lang') || null,
    argv: process.argv
  };
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({ show: false, webPreferences: { nodeIntegration: false, contextIsolation: true } });
  await win.loadFile('index.html');
  const renderer = await win.webContents.executeJavaScript(`(() => {
    const dt = Intl.DateTimeFormat().resolvedOptions();
    return {
      navigatorLanguage: navigator.language,
      navigatorLanguages: navigator.languages,
      intlLocale: dt.locale,
      intlTZ: dt.timeZone,
      date: new Date().toString(),
      htmlLang: document.documentElement.lang
    };
  })()`);
  const result = { main: mainSnapshot(), renderer };
  fs.writeFileSync(process.env.PG_OUT, JSON.stringify(result, null, 2), 'utf8');
  app.quit();
}).catch((e) => {
  fs.writeFileSync(process.env.PG_OUT, JSON.stringify({ error: String(e), stack: e.stack }, null, 2), 'utf8');
  app.quit();
});
