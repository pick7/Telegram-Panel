window.telegramPanel = window.telegramPanel || {};

window.telegramPanel.copyText = async (text) => {
  if (text === null || text === undefined) return;
  const value = String(text);

  try {
    if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(value);
      return;
    }
  } catch (_) {
    // ignore and fallback
  }

  try {
    const textarea = document.createElement("textarea");
    textarea.value = value;
    textarea.setAttribute("readonly", "");
    textarea.setAttribute("aria-hidden", "true");
    textarea.style.position = "fixed";
    textarea.style.top = "0";
    textarea.style.left = "0";
    textarea.style.width = "2em";
    textarea.style.height = "2em";
    textarea.style.padding = "0";
    textarea.style.border = "none";
    textarea.style.outline = "none";
    textarea.style.boxShadow = "none";
    textarea.style.background = "transparent";
    textarea.style.opacity = "0";

    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    // iOS Safari 需要显式 setSelectionRange
    textarea.setSelectionRange(0, textarea.value.length);

    const ok = document.execCommand("copy");
    document.body.removeChild(textarea);
    if (ok) return;
  } catch (_) {
    // ignore and fallback
  }

  // 最终兜底：弹窗可手动复制（在部分移动端/非安全上下文下更稳定）
  try {
    window.prompt("复制以下内容：", value);
  } catch (_) {
    // ignore
  }
};

