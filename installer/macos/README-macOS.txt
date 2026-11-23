Plana for macOS（预览体验版）

一、安装与首次运行
1. 解压整个压缩包，保持 Plana.app、首次运行.command、安装说明.txt 在同一层目录。
2. 双击「首次运行.command」。
   - 如果系统提示“无法验证开发者”，请右键点击该文件，选择“打开”。
   - 如果仍然打不开，打开“终端”，进入解压后的文件夹，执行：
       bash 首次运行.command
3. 脚本会自动清除隔离属性、补可执行权限并做本地 ad-hoc 签名，然后启动 Plana。
4. 首次启动后，建议把 Plana.app 拖到“应用程序”文件夹。

二、版本选择
- Plana-mac-arm64-*.zip：Apple Silicon（M1 / M2 / M3 / M4）使用。
- Plana-mac-x64-*.zip：Intel Mac 使用。

三、当前 macOS 预览版限制
- 暂无 Apple Developer ID 签名与公证，首次运行需通过「首次运行.command」解除隔离属性。
- 系统通知通过 osascript 触发，通知来源可能显示为“脚本编辑器”；点击通知不会跳回应用。
- 开机自启、小组件点击穿透、剪贴板图片/文件粘贴暂未实现（完整体验请使用 Windows 版）。
- 数据保存在 ~/.local/share/VGList，首次启动为干净空数据。

四、如果启动失败
请在“终端”执行下面三行（把路径换成你实际解压的位置）：
  xattr -cr ~/Downloads/Plana-mac-arm64/Plana.app
  codesign --force --deep --sign - ~/Downloads/Plana-mac-arm64/Plana.app
  open ~/Downloads/Plana-mac-arm64/Plana.app
