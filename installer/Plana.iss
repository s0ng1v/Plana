; Plana Windows 安装程序脚本（Inno Setup 6，ADR-0060）
; 特点：
; 1. 免管理员权限安装到当前用户目录（%LOCALAPPDATA%\Programs\Plana）；
; 2. 固定 AppId，后续新版本直接覆盖安装升级，自动保留原有安装路径与快捷方式；
; 3. 用户数据全部存于 %LOCALAPPDATA%\VGList\，覆盖安装或卸载均绝不删除用户数据。

#ifndef MyAppVersion
  #define MyAppVersion "0.1.10"
#endif

#ifndef StageDir
  #define StageDir "..\dist\stage"
#endif

#define MyAppName "Plana"
#define MyAppPublisher "Plana"
#define MyAppExeName "VGList.exe"

[Setup]
AppId={{8C7E2A41-9B3D-4F6E-A512-7D3E91F0B4C2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Plana-Setup-{#MyAppVersion}
SetupIconFile=..\src\VGList.Desktop\Assets\Plana\plana.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=VGList.exe,VGList.Mcp.exe
RestartApplications=no
AppMutex=VGList.Desktop.SingleInstance

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=安装 — {#MyAppName}
WelcomeLabel1=欢迎使用 {#MyAppName} 安装向导
WelcomeLabel2=即将在你的电脑上安装 {#MyAppName} {#MyAppVersion}。%n%n如已安装旧版本，直接继续即可覆盖升级，你的任务、设置、模型配置与 Lana 记忆都会完整保留。
ClickNext=点击「下一步」继续，或点击「取消」退出安装。
ButtonNext=下一步(&N) >
ButtonBack=< 上一步(&B)
ButtonInstall=安装(&I)
ButtonFinish=完成(&F)
ButtonCancel=取消
WizardSelectDir=选择安装位置
SelectDirDesc={#MyAppName} 将安装到以下文件夹。
SelectDirLabel3=安装程序将把 {#MyAppName} 安装至下列文件夹（后续升级会自动沿用此路径）。
SelectDirBrowseLabel=点击「下一步」继续，或点击「浏览」选择其他文件夹。
ButtonBrowse=浏览(&R)...
WizardSelectTasks=选择附加选项
SelectTasksDesc=请选择需要执行的附加操作。
SelectTasksLabel2=选择安装 {#MyAppName} 时需要创建的快捷方式，然后点击「下一步」。
WizardReady=准备安装
ReadyLabel1=安装程序已准备好将 {#MyAppName} 安装到你的电脑。
ReadyLabel2a=点击「安装」开始安装；如需修改设置，请点击「上一步」。
WizardInstalling=正在安装
InstallingLabel=正在安装 {#MyAppName}，请稍候…
FinishedHeadingLabel={#MyAppName} 安装完成
FinishedLabelNoIcons={#MyAppName} 已成功安装到你的电脑。
FinishedLabel={#MyAppName} 已成功安装到你的电脑，可通过桌面或开始菜单快捷方式启动。
ClickFinish=点击「完成」退出安装向导。
ExitSetupTitle=退出安装
ExitSetupMessage=安装尚未完成，确定要现在退出吗？

[CustomMessages]
CreateDesktopIcon=创建桌面快捷方式
LaunchAfterInstall=安装完成后立即启动 Plana

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "快捷方式："

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Plana · 桌面任务与日程"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Plana · 桌面任务与日程"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  // 覆盖升级时若旧版 Plana 藏在系统托盘中，先温和关闭，确保 SQLite WAL 安全落盘且不锁文件
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM VGList.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM VGList.Mcp.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(600);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM VGList.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM VGList.Mcp.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;
