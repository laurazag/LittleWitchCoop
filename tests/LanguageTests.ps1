$ErrorActionPreference = 'Stop'
$source=Get-Content (Join-Path $PSScriptRoot '../src/L10n.cs') -Raw
Add-Type ($source + @"
public static class LanguageProbe {
 public static void Run() {
  if (LittleWitchCoop.L10n.Resolve("auto", "pt-BR") != "pt-BR" || LittleWitchCoop.L10n.Resolve("auto", "pt-PT") != "pt-BR" || LittleWitchCoop.L10n.Resolve("auto", "ja-JP") != "en" || LittleWitchCoop.L10n.Resolve("en", "pt-BR") != "en" || LittleWitchCoop.L10n.Resolve("pt-BR", "en-US") != "pt-BR" || LittleWitchCoop.L10n.Resolve("invalid", "en-US") != "en") throw new System.Exception("Language resolution failed");
  LittleWitchCoop.L10n.Language="en";
  if(LittleWitchCoop.L10n.T("Conexão") != "Connection" || LittleWitchCoop.L10n.T("Mara 💚") != "Mara 💚") throw new System.Exception("Translation failed");
  LittleWitchCoop.L10n.Language="pt-BR";
  if(LittleWitchCoop.L10n.T("Conexão") != "Conexão") throw new System.Exception("Manual switch failed");
 }
}
"@)
[LanguageProbe]::Run()
'PASS Windows detection, fallback, manual override, live switch and unchanged unknown text'

