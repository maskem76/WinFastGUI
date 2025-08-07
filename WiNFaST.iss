; Inno Setup Script for WiNFaST
; Generated with Wizard and finalized by Gemini

; --- UYGULAMA BİLGİLERİ (BURAYI KONTROL ET) ---
#define MyAppName "WiNFaST"
#define MyAppVersion "1.5.0.0"
#define MyAppPublisher "LeVeNTB"
#define MyAppExeName "WinFastGUI.exe"

[Setup]
; Benzersiz uygulama kimliği, dokunma.
AppId={{953D8D11-D60D-4771-B9C4-087BFB61FA57}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes

; --- KRİTİK AYARLAR ---
; Programın çalışması için Yönetici iznini zorunlu kılar.
PrivilegesRequired=admin 
; Kullanıcının bu seçimi es geçmesine izin vermez.
PrivilegesRequiredOverridesAllowed=commandline

; --- ÇIKTI AYARLARI ---
; Setup.exe'nin kaydedileceği klasör (klasör yoksa oluşturulur).
OutputDir=C:\WinFast_Kurulum_Ciktisi 
; Oluşturulacak setup dosyasının adı.
OutputBaseFilename=WiNFaST_v{#MyAppVersion}_Kurulum 
SolidCompression=yes
WizardStyle=modern

[Languages]
; Kurulum sihirbazının dilini Türkçe yapar.
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
; Kurulum sırasında "Masaüstü kısayolu oluştur" seçeneği sunar.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; --- KAYNAK DOSYALAR (BURAYI KONTROL ET) ---
; Program dosyalarının bulunduğu klasör.
; Visual Studio'dan Yayımla/Publish yaptığın dosyaların bu klasörde olduğundan emin ol.
Source: "C:\winfast\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOT: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
; --- KISAYOL AYARLARI ---
; Başlat Menüsü'ne programın kısayolunu ekler.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Başlat Menüsü'ne "Kaldır" kısayolunu ekler.
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; Eğer kullanıcı seçerse, Masaüstü'ne kısayol ekler.
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; --- ÖZEL MESAJLAR ---
[Messages]
BeveledLabel=WiNFaST by LeVeNTB
WelcomeLabel1=[name] Kurulum Sihirbazına Hoş Geldiniz
WelcomeLabel2=[name/ver] uygulamasını bilgisayarınıza kurmaya hazırız.

