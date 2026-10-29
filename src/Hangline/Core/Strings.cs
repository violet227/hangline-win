using System.Globalization;

namespace Hangline.Core;

/// The words on screen, in English and Simplified Chinese, following the
/// system language. Ported from the original's Localizable.strings.
internal static class Strings
{
    private static readonly bool Chinese =
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static string L(string english) =>
        Chinese && ZhHans.TryGetValue(english, out var zh) ? zh : english;

    private static readonly Dictionary<string, string> ZhHans = new()
    {
        ["Let Hangline handle your screenshots?"] = "让 Hangline 接管你的截图？",
        ["Screenshots will hang on the line the instant you take them, and will not pile up in your Screenshots folder. Drag one to a folder to keep it, or discard it with the cross. You can turn this off from the tray icon."] =
            "你截图的那一瞬间，图片就会挂到晾衣绳上，也不会堆在截图文件夹里。拖进文件夹即保留，点叉即丢弃。随时可以从托盘图标关掉。",
        ["Turn on"] = "开启",
        ["Not now"] = "以后再说",
        ["Hide line"] = "隐藏晾衣绳",
        ["Show line"] = "显示晾衣绳",
        ["Take everything down"] = "全部取下",
        ["Handle screenshots"] = "接管截图",
        ["Open screenshots folder"] = "打开截图文件夹",
        ["Sounds"] = "声音",
        ["Open at login"] = "登录时打开",
        ["Quit Hangline"] = "退出 Hangline",
        ["Keep on line"] = "绳上保留",
        ["As many as fit"] = "放得下的数量",
        ["{0} photos"] = "{0} 张",
        ["Also watch folders"] = "同时监视文件夹",
        ["Add Folder…"] = "添加文件夹…",
        ["Stop Watching"] = "停止监视",
        ["New images saved to this folder will hang on the line."] = "保存到此文件夹的新图片会挂到晾衣绳上。",
        ["Shortcut"] = "快捷键",
        ["Change Shortcut…"] = "更改快捷键…",
        ["Change Shortcut"] = "更改快捷键",
        ["None"] = "无",
        ["Press the new shortcut"] = "请按下新的快捷键",
        ["Escape cancels. Delete removes the shortcut."] = "按 Esc 取消，按删除键移除快捷键。",
        ["That shortcut is taken by another app."] = "这个快捷键已被其他应用占用。",
        ["Size"] = "大小",
        ["Small"] = "小",
        ["Medium"] = "中",
        ["Large"] = "大",
        ["Bring the line down"] = "放下晾衣绳",
        ["When the pointer rests at the top edge"] = "指针停在屏幕顶边时",
        ["When something new hangs"] = "有新内容挂上时",
        ["Over full screen apps"] = "在全屏应用上",
        ["Hang copied images"] = "挂上复制的图片",
        ["Hangline, hanging copied images"] = "Hangline，正在挂上复制的图片",
        ["The line is empty"] = "晾衣绳上是空的",
        ["{0} on the line"] = "晾衣绳上有 {0} 张",
        ["Copy"] = "复制",
        ["Open"] = "打开",
        ["Quick Look"] = "快速查看",
        ["Markup"] = "标记",
        ["Show in Explorer"] = "在资源管理器中显示",
        ["Save to Desktop"] = "存储到桌面",
        ["Discard"] = "丢弃",
        ["Take down"] = "取下",
        ["Move to Recycle Bin"] = "移到回收站",
        ["Take down everything to the left"] = "取下左侧全部",
        ["Take down everything to the right"] = "取下右侧全部",
        ["Take a screenshot and it will hang here"] = "截个图，它就会挂在这里",
        ["Copied"] = "已复制",
    };
}
