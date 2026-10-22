Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!ifndef PAYLOAD_DIR
 !define PAYLOAD_DIR "..\artifacts\ImageRescue-v0.4.0-win-x64"
!endif
!ifndef OUTPUT_FILE
 !define OUTPUT_FILE "..\artifacts\ImageRescue-v0.4.0-Setup.exe"
!endif
Name "ImageRescue 0.4.0"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\ImageRescue"
InstallDirRegKey HKLM "Software\ImageRescue" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 32
BrandingText "ImageRescue • Open-source photo recovery"
VIProductVersion "0.4.0.0"
VIAddVersionKey "ProductName" "ImageRescue"
VIAddVersionKey "FileDescription" "ImageRescue installer"
VIAddVersionKey "FileVersion" "0.4.0"
VIAddVersionKey "LegalCopyright" "Copyright Ali Jaber. MIT License."
!define MUI_ICON "..\src\ImageRescue.App\Assets\ImageRescue.ico"
!define MUI_UNICON "..\src\ImageRescue.App\Assets\ImageRescue.ico"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_LANGDLL_REGISTRY_ROOT "HKLM"
!define MUI_LANGDLL_REGISTRY_KEY "Software\ImageRescue"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Arabic"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "Spanish"
!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "Portuguese"
!insertmacro MUI_LANGUAGE "Hindi"
!insertmacro MUI_LANGUAGE "Japanese"
LangString DesktopText ${LANG_ENGLISH} "Desktop shortcut"
LangString DesktopText ${LANG_ARABIC} "اختصار سطح المكتب"
LangString DesktopText ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString DesktopText ${LANG_RUSSIAN} "Ярлык на рабочем столе"
LangString DesktopText ${LANG_SPANISH} "Acceso directo en el escritorio"
LangString DesktopText ${LANG_FRENCH} "Raccourci sur le bureau"
LangString DesktopText ${LANG_GERMAN} "Desktop-Verknüpfung"
LangString DesktopText ${LANG_PORTUGUESE} "Atalho na área de trabalho"
LangString DesktopText ${LANG_HINDI} "डेस्कटॉप शॉर्टकट"
LangString DesktopText ${LANG_JAPANESE} "デスクトップのショートカット"

Function .onInit
 ${IfNot} ${RunningX64}
  MessageBox MB_ICONSTOP "ImageRescue requires 64-bit Windows."
  Abort
 ${EndIf}
 SetRegView 64
 SetShellVarContext all
 !insertmacro MUI_LANGDLL_DISPLAY
 Call CheckAppClosed
FunctionEnd

Function .onVerifyInstDir
 ${GetFileName} "$INSTDIR" $0
 ${If} $0 != "ImageRescue"
  Abort
 ${EndIf}
FunctionEnd

Function CheckAppClosed
 FindWindow $0 "" "ImageRescue 0.4.0"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before continuing."
  Abort
 ${EndIf}
 FindWindow $0 "" "ImageRescue 0.3.1"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before continuing."
  Abort
 ${EndIf}
 FindWindow $0 "" "ImageRescue 0.3"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before installing this update."
  Abort
 ${EndIf}
 FindWindow $0 "" "ImageRescue"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before installing this update."
  Abort
 ${EndIf}
FunctionEnd

Section "ImageRescue" MainSection
 SectionIn RO
 SetRegView 64
 SetShellVarContext all
 SetOutPath "$INSTDIR"
 File /r "${PAYLOAD_DIR}\*"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 CreateDirectory "$SMPROGRAMS\ImageRescue"
 CreateShortCut "$SMPROGRAMS\ImageRescue\ImageRescue.lnk" "$INSTDIR\ImageRescue.App.exe" "" "$INSTDIR\ImageRescue.App.exe" 0
 CreateShortCut "$SMPROGRAMS\ImageRescue\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
 WriteRegStr HKLM "Software\ImageRescue" "InstallDir" "$INSTDIR"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "DisplayName" "ImageRescue 0.4.0"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "DisplayVersion" "0.4.0"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "Publisher" "Ali Jaber"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "DisplayIcon" "$INSTDIR\ImageRescue.App.exe"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "InstallLocation" "$INSTDIR"
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
 WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
 WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "NoModify" 1
 WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue" "NoRepair" 1
SectionEnd

Section "$(DesktopText)" DesktopSection
 SetShellVarContext all
 CreateShortCut "$DESKTOP\ImageRescue.lnk" "$INSTDIR\ImageRescue.App.exe" "" "$INSTDIR\ImageRescue.App.exe" 0
SectionEnd

Function un.onInit
 SetRegView 64
 SetShellVarContext all
 !insertmacro MUI_UNGETLANGUAGE
 FindWindow $0 "" "ImageRescue 0.4.0"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before continuing."
  Abort
 ${EndIf}
 FindWindow $0 "" "ImageRescue 0.3.1"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before continuing."
  Abort
 ${EndIf}
 FindWindow $0 "" "ImageRescue 0.3"
 ${If} $0 != 0
  MessageBox MB_ICONEXCLAMATION "Close ImageRescue before uninstalling."
  Abort
 ${EndIf}
FunctionEnd

Section "Uninstall"
 SetRegView 64
 SetShellVarContext all
 ; Generated list: delete only files shipped in this release. Never recursively
 ; delete the install directory: users may have placed recovery output inside it.
 !include "UninstallFiles.nsh"
 Delete "$INSTDIR\Uninstall.exe"
 Delete "$DESKTOP\ImageRescue.lnk"
 Delete "$SMPROGRAMS\ImageRescue\ImageRescue.lnk"
 Delete "$SMPROGRAMS\ImageRescue\Uninstall.lnk"
 RMDir "$SMPROGRAMS\ImageRescue"
 RMDir "$INSTDIR"
 DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ImageRescue"
 DeleteRegKey HKLM "Software\ImageRescue"
SectionEnd
