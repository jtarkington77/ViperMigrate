using System.Text;
using System.Web;
using ViperMigrate.Core.Models;

namespace ViperMigrate.Core.Common;

public static class HtmlReportGenerator
{
    // Virtual printer patterns to filter from report
    private static readonly string[] VirtualPrinterPatterns =
    {
        "Microsoft Print to PDF", "Microsoft XPS Document Writer",
        "OneNote", "Send to OneNote", "Fax", "Foxit", "CutePDF",
        "Adobe PDF", "PDFCreator", "PDF24", "doPDF"
    };

    public static string Generate(
        List<CategoryResult> results,
        List<ManualAction> manualActions,
        MachineInfo? sourceInfo = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>ViperMigrate Report</title>");
        sb.AppendLine("<style>");
        AppendCss(sb);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>ViperMigrate Report</h1>");
        if (sourceInfo != null)
        {
            sb.AppendLine($"<p class=\"meta\">Source: {E(sourceInfo.ComputerName)} | User: {E(sourceInfo.Domain)}\\{E(sourceInfo.UserName)} | OS: {E(sourceInfo.OsVersion)}</p>");
        }
        sb.AppendLine($"<p class=\"meta\">Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        sb.AppendLine("</div>");

        // Status cards
        var successCount = results.Count(r => r.Status == CategoryStatus.Success);
        var warnCount = results.Count(r => r.Status == CategoryStatus.PartialSuccess);
        var failCount = results.Count(r => r.Status == CategoryStatus.Failed);

        sb.AppendLine("<div class=\"cards\">");
        sb.AppendLine($"<div class=\"card success\"><span class=\"num\">{successCount}</span><span class=\"label\">Success</span></div>");
        sb.AppendLine($"<div class=\"card warning\"><span class=\"num\">{warnCount}</span><span class=\"label\">Warnings</span></div>");
        sb.AppendLine($"<div class=\"card error\"><span class=\"num\">{failCount}</span><span class=\"label\">Failed</span></div>");
        sb.AppendLine("</div>");

        // Filter toolbar
        sb.AppendLine("<div class=\"toolbar\">");
        sb.AppendLine("<button class=\"filter-btn active\" onclick=\"filterAll()\">Show All</button>");
        sb.AppendLine("<button class=\"filter-btn\" onclick=\"filterFailures()\">Failures Only</button>");
        sb.AppendLine("<button class=\"filter-btn\" onclick=\"filterManual()\">Manual Actions Only</button>");
        if (manualActions.Any(a => !string.IsNullOrEmpty(a.Command) && a.Command.StartsWith("winget")))
        {
            sb.AppendLine("<button class=\"copy-btn\" onclick=\"copyAllWinget()\">Copy All Winget Commands</button>");
        }
        sb.AppendLine("<button class=\"complete-btn\" onclick=\"generateTechReport()\">Complete</button>");
        sb.AppendLine("</div>");

        // Detailed results
        sb.AppendLine("<h2>Results by Category</h2>");
        foreach (var r in results)
        {
            var statusClass = r.Status switch
            {
                CategoryStatus.Success => "status-success",
                CategoryStatus.PartialSuccess => "status-warning",
                CategoryStatus.Failed => "status-error",
                _ => "status-skipped"
            };
            var statusIcon = r.Status switch
            {
                CategoryStatus.Success => "&#10004;",
                CategoryStatus.PartialSuccess => "&#9888;",
                CategoryStatus.Failed => "&#10008;",
                _ => "&#8212;"
            };

            sb.AppendLine($"<details class=\"category {statusClass}\" data-status=\"{r.Status}\" open>");
            sb.AppendLine($"<summary><span class=\"icon\">{statusIcon}</span> {E(r.Category)} &mdash; {r.ItemsProcessed}/{r.ItemsTotal} processed");
            if (r.Duration != TimeSpan.Zero) sb.Append($" ({r.Duration.TotalSeconds:F1}s)");
            sb.AppendLine("</summary>");

            if (r.Warnings.Count > 0)
            {
                sb.AppendLine("<ul class=\"warnings\">");
                foreach (var w in r.Warnings)
                    sb.AppendLine($"<li>{E(w)}</li>");
                sb.AppendLine("</ul>");
            }

            // Inline manual actions for this category
            var categoryActions = r.ManualActions
                .Where(a => !IsVirtualPrinterAction(a))
                .ToList();
            if (categoryActions.Count > 0)
            {
                sb.AppendLine($"<div class=\"manual-section\" data-category=\"{E(r.Category)}\">");
                sb.AppendLine($"<h4>Manual Actions ({categoryActions.Count})</h4>");
                foreach (var action in categoryActions)
                {
                    AppendManualAction(sb, action);
                }
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</details>");
        }

        // Standalone manual actions (from package level)
        var standaloneActions = manualActions
            .Where(a => !results.Any(r => r.ManualActions.Contains(a)))
            .Where(a => !IsVirtualPrinterAction(a))
            .ToList();

        if (standaloneActions.Count > 0)
        {
            sb.AppendLine("<h2>Additional Manual Actions</h2>");
            sb.AppendLine("<div class=\"manual-section\" id=\"manual-actions\">");
            sb.AppendLine("<div class=\"toolbar-mini\">");
            sb.AppendLine("<label><input type=\"checkbox\" id=\"hideCompleted\" onchange=\"toggleCompleted()\"> Hide completed</label>");
            sb.AppendLine("</div>");
            foreach (var action in standaloneActions)
            {
                AppendManualAction(sb, action);
            }
            sb.AppendLine("</div>");
        }

        // JavaScript
        sb.AppendLine("<script>");
        AppendJs(sb);
        sb.AppendLine("</script>");

        // Tech Report Overlay
        sb.AppendLine("<div id=\"techOverlay\" class=\"tech-overlay\" style=\"display:none\" onclick=\"closeTechReportOnBg(event)\">");
        sb.AppendLine("<div class=\"tech-report\">");
        sb.AppendLine("<div class=\"tech-toolbar\">");
        sb.AppendLine("<button class=\"copy-btn\" onclick=\"printTechReport()\">Print</button>");
        sb.AppendLine("<button class=\"copy-btn\" onclick=\"copyTechReport()\">Copy to Clipboard</button>");
        sb.AppendLine("<button class=\"filter-btn\" onclick=\"closeTechReport()\">Close</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div id=\"techContent\"></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void AppendManualAction(StringBuilder sb, ManualAction action)
    {
        var id = $"action_{Guid.NewGuid():N}";
        sb.AppendLine($"<div class=\"action-item\" data-id=\"{id}\">");
        sb.AppendLine($"<label><input type=\"checkbox\" class=\"action-check\" data-id=\"{id}\" onchange=\"saveCheck(this)\">");
        sb.AppendLine($"<span class=\"priority-{action.Priority.ToString().ToLower()}\">[{action.Priority}]</span> ");
        sb.AppendLine($"<strong>{E(action.Title)}</strong></label>");
        sb.AppendLine($"<p class=\"action-desc\">{E(action.Description).Replace("\n", "<br>")}</p>");
        if (!string.IsNullOrEmpty(action.Command))
        {
            sb.AppendLine($"<div class=\"cmd-row\"><code>{E(action.Command)}</code>");
            sb.AppendLine($"<button class=\"copy-btn small\" onclick=\"copyCmd(this, '{EscapeJs(action.Command)}')\">Copy</button></div>");
        }
        sb.AppendLine("</div>");
    }

    private static bool IsVirtualPrinterAction(ManualAction action)
    {
        return VirtualPrinterPatterns.Any(v =>
            action.Title.Contains(v, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendCss(StringBuilder sb)
    {
        sb.AppendLine(@"
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #0B1519; color: #E0F0F0; padding: 24px; line-height: 1.5; }
.header { margin-bottom: 24px; border-bottom: 1px solid #1A3A3A; padding-bottom: 16px; }
h1 { color: #4DD0B8; font-size: 28px; margin-bottom: 4px; }
h2 { color: #E0F0F0; font-size: 20px; margin: 24px 0 12px; }
h4 { color: #E0F0F0; margin-bottom: 8px; }
.meta { color: #7A9E9E; font-size: 13px; }
.cards { display: flex; gap: 16px; margin-bottom: 20px; }
.card { padding: 16px 24px; border-radius: 8px; text-align: center; min-width: 120px; }
.card .num { display: block; font-size: 32px; font-weight: bold; }
.card .label { font-size: 13px; color: #7A9E9E; }
.card.success { background: #0a2418; border: 1px solid #0d3d2a; }
.card.success .num { color: #00C853; }
.card.warning { background: #1a1800; border: 1px solid #3d3500; }
.card.warning .num { color: #FFB300; }
.card.error { background: #1a0a0a; border: 1px solid #3d1515; }
.card.error .num { color: #FF5252; }
.toolbar { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center; }
.toolbar-mini { margin-bottom: 12px; color: #7A9E9E; font-size: 13px; }
.filter-btn { background: #101D22; color: #E0F0F0; border: 1px solid #1A3A3A; padding: 6px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; transition: all 0.2s; }
.filter-btn:hover { background: #1A3A3A; border-color: #4DD0B8; }
.filter-btn.active { background: #4DD0B8; color: #0B1519; border-color: #4DD0B8; font-weight: 600; }
.copy-btn { background: #0F2027; color: #4DD0B8; border: 1px solid #4DD0B8; padding: 6px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; transition: all 0.2s; }
.copy-btn:hover { background: #4DD0B8; color: #0B1519; }
.copy-btn.small { padding: 2px 8px; font-size: 11px; margin-left: 8px; }
.complete-btn { background: #4DD0B8; color: #0B1519; border: 1px solid #4DD0B8; padding: 8px 20px; border-radius: 4px; cursor: pointer; font-size: 14px; font-weight: 600; transition: all 0.2s; margin-left: auto; }
.complete-btn:hover { background: #6EE0CC; border-color: #6EE0CC; }
details.category { background: #101D22; border-radius: 8px; margin-bottom: 8px; padding: 12px 16px; border-left: 4px solid #1A3A3A; }
details.status-success { border-left-color: #00C853; }
details.status-warning { border-left-color: #FFB300; }
details.status-error { border-left-color: #FF5252; }
details.status-skipped { border-left-color: #7A9E9E; }
summary { cursor: pointer; font-size: 15px; font-weight: 600; list-style: none; }
summary::-webkit-details-marker { display: none; }
summary .icon { margin-right: 8px; }
.warnings { margin: 8px 0 0 24px; color: #FFB300; font-size: 13px; }
.warnings li { margin-bottom: 2px; }
.manual-section { margin-top: 12px; }
.action-item { background: #0F2027; border-radius: 6px; padding: 12px; margin-bottom: 8px; border: 1px solid #1A3A3A; transition: opacity 0.2s; }
.action-item.completed { opacity: 0.5; }
.action-item label { cursor: pointer; display: flex; align-items: center; gap: 8px; }
.action-check { width: 16px; height: 16px; accent-color: #4DD0B8; }
.action-desc { color: #7A9E9E; font-size: 13px; margin: 6px 0 0 28px; white-space: pre-wrap; }
.cmd-row { display: flex; align-items: center; margin: 6px 0 0 28px; }
.cmd-row code { background: #080f12; padding: 4px 8px; border-radius: 4px; font-size: 12px; color: #4DD0B8; border: 1px solid #1A3A3A; }
.priority-high { color: #FF5252; font-size: 11px; }
.priority-medium { color: #FFB300; font-size: 11px; }
.priority-low { color: #7A9E9E; font-size: 11px; }
.tech-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); z-index: 1000; overflow-y: auto; padding: 40px 20px; }
.tech-report { max-width: 900px; margin: 0 auto; background: #0B1519; border-radius: 8px; padding: 32px; border: 1px solid #1A3A3A; }
.tech-toolbar { display: flex; gap: 8px; margin-bottom: 24px; justify-content: flex-end; }
.tech-report h2 { color: #4DD0B8; font-size: 22px; margin: 0 0 4px; }
.tech-report .tech-subtitle { color: #7A9E9E; font-size: 13px; margin-bottom: 20px; }
.tech-report .info-grid { display: grid; grid-template-columns: 160px 1fr; gap: 4px 16px; margin-bottom: 20px; font-size: 13px; }
.tech-report .info-label { color: #7A9E9E; font-weight: 600; }
.tech-report .info-value { color: #E0F0F0; }
.tech-section { background: #101D22; border-radius: 6px; margin-bottom: 8px; border: 1px solid #1A3A3A; }
.tech-section summary { padding: 10px 16px; font-size: 14px; font-weight: 600; color: #E0F0F0; cursor: pointer; list-style: none; display: flex; justify-content: space-between; align-items: center; }
.tech-section summary::-webkit-details-marker { display: none; }
.tech-section summary .count { color: #7A9E9E; font-weight: normal; font-size: 12px; }
.tech-section summary::after { content: '\25BC'; font-size: 10px; color: #7A9E9E; transition: transform 0.2s; }
.tech-section[open] summary::after { transform: rotate(180deg); }
.tech-section .section-body { padding: 0 16px 12px; }
.tech-section table { width: 100%; border-collapse: collapse; font-size: 13px; }
.tech-section th { text-align: left; padding: 6px 8px; color: #7A9E9E; border-bottom: 1px solid #1A3A3A; font-weight: 600; }
.tech-section td { padding: 5px 8px; color: #E0F0F0; border-bottom: 1px solid #0F2027; }
.tech-section .status-ok { color: #00C853; }
.tech-section .status-warn { color: #FFB300; }
.tech-section .status-fail { color: #FF5252; }
.tech-section ul { margin: 0; padding: 0; list-style: none; }
.tech-section li { padding: 4px 8px; font-size: 13px; color: #E0F0F0; border-bottom: 1px solid #0F2027; }
.tech-section li:last-child { border-bottom: none; }
.tech-section li .check-icon { color: #00C853; margin-right: 6px; }
.tech-summary-grid { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 12px; margin-bottom: 20px; }
.tech-stat { background: #101D22; border-radius: 6px; padding: 12px 16px; text-align: center; border: 1px solid #1A3A3A; }
.tech-stat .stat-num { display: block; font-size: 24px; font-weight: bold; }
.tech-stat .stat-label { font-size: 11px; color: #7A9E9E; text-transform: uppercase; letter-spacing: 0.5px; }
.tech-stat.ok .stat-num { color: #00C853; }
.tech-stat.warn .stat-num { color: #FFB300; }
.tech-stat.fail .stat-num { color: #FF5252; }
@media print { body { background: white; color: #333; } h1 { color: #2D8A78; } .toolbar, .copy-btn, .filter-btn, .complete-btn { display: none; } .card { border: 1px solid #ccc; } details.category { border: 1px solid #ccc; } }
body.printing-tech > *:not(.tech-overlay) { display: none !important; }
body.printing-tech .tech-overlay { position: static; background: white; padding: 20px; }
body.printing-tech .tech-report { background: white; border: none; color: #333; max-width: 100%; padding: 0; }
body.printing-tech .tech-report h2 { color: #2D8A78; }
body.printing-tech .tech-report .tech-subtitle { color: #666; }
body.printing-tech .tech-section { background: white; border-color: #ccc; }
body.printing-tech .tech-section summary { color: #333; }
body.printing-tech .tech-section th { color: #333; border-color: #ccc; }
body.printing-tech .tech-section td { color: #333; border-color: #eee; }
body.printing-tech .tech-section li { color: #333; border-color: #eee; }
body.printing-tech .tech-report .info-label { color: #666; }
body.printing-tech .tech-report .info-value { color: #333; }
body.printing-tech .tech-stat { background: #f5f5f5; border-color: #ccc; }
body.printing-tech .tech-summary-grid { gap: 8px; }
body.printing-tech .tech-toolbar { display: none; }
");
    }

    private static void AppendJs(StringBuilder sb)
    {
        sb.AppendLine(@"
// Restore checkbox state from localStorage
document.addEventListener('DOMContentLoaded', function() {
    document.querySelectorAll('.action-check').forEach(function(cb) {
        var saved = localStorage.getItem('viper_' + cb.dataset.id);
        if (saved === 'true') {
            cb.checked = true;
            cb.closest('.action-item').classList.add('completed');
        }
    });
});

function saveCheck(cb) {
    localStorage.setItem('viper_' + cb.dataset.id, cb.checked);
    var item = cb.closest('.action-item');
    if (cb.checked) item.classList.add('completed');
    else item.classList.remove('completed');
}

function toggleCompleted() {
    var hide = document.getElementById('hideCompleted').checked;
    document.querySelectorAll('.action-item.completed').forEach(function(el) {
        el.style.display = hide ? 'none' : '';
    });
}

function filterAll() {
    setActiveFilter(0);
    document.querySelectorAll('details.category').forEach(function(el) { el.style.display = ''; });
    document.querySelectorAll('.manual-section').forEach(function(el) { el.style.display = ''; });
    document.querySelectorAll('h2').forEach(function(el) { el.style.display = ''; });
}

function filterFailures() {
    setActiveFilter(1);
    document.querySelectorAll('details.category').forEach(function(el) {
        var s = el.dataset.status;
        el.style.display = (s === 'Failed' || s === 'PartialSuccess') ? '' : 'none';
    });
    document.querySelectorAll('#manual-actions').forEach(function(el) { el.style.display = 'none'; });
}

function filterManual() {
    setActiveFilter(2);
    document.querySelectorAll('details.category').forEach(function(el) {
        el.style.display = el.querySelector('.manual-section') ? '' : 'none';
    });
    document.querySelectorAll('#manual-actions').forEach(function(el) { el.style.display = ''; });
}

function setActiveFilter(idx) {
    document.querySelectorAll('.filter-btn').forEach(function(b, i) {
        b.classList.toggle('active', i === idx);
    });
}

function copyCmd(btn, cmd) {
    navigator.clipboard.writeText(cmd).then(function() {
        var orig = btn.textContent;
        btn.textContent = 'Copied!';
        setTimeout(function() { btn.textContent = orig; }, 1500);
    });
}

function copyAllWinget() {
    var cmds = [];
    document.querySelectorAll('.cmd-row code').forEach(function(el) {
        var t = el.textContent.trim();
        if (t.startsWith('winget')) cmds.push(t);
    });
    navigator.clipboard.writeText(cmds.join('\n')).then(function() {
        var btn = event.target;
        var orig = btn.textContent;
        btn.textContent = 'Copied ' + cmds.length + ' commands!';
        setTimeout(function() { btn.textContent = orig; }, 2000);
    });
}

function generateTechReport() {
    var html = '';
    html += '<h2>Workstation Migration &mdash; Completion Report</h2>';
    html += '<p class=""tech-subtitle"">ViperMigrate Tech Summary</p>';

    // Machine info
    var metaEls = document.querySelectorAll('.header .meta');
    html += '<div class=""info-grid"">';
    metaEls.forEach(function(el) {
        var text = el.textContent.trim();
        if (text.startsWith('Source:')) {
            var parts = text.split('|');
            parts.forEach(function(p) {
                var kv = p.trim().split(':');
                if (kv.length >= 2) {
                    html += '<span class=""info-label"">' + kv[0].trim() + '</span>';
                    html += '<span class=""info-value"">' + kv.slice(1).join(':').trim() + '</span>';
                }
            });
        }
        if (text.startsWith('Generated:')) {
            html += '<span class=""info-label"">Migration Date</span>';
            html += '<span class=""info-value"">' + text.replace('Generated: ', '') + '</span>';
        }
    });
    html += '<span class=""info-label"">Completed</span>';
    html += '<span class=""info-value"">' + new Date().toLocaleString() + '</span>';
    html += '</div>';

    // Summary stats
    var cards = document.querySelectorAll('.card .num');
    var sN = cards[0] ? cards[0].textContent : '0';
    var wN = cards[1] ? cards[1].textContent : '0';
    var fN = cards[2] ? cards[2].textContent : '0';
    html += '<div class=""tech-summary-grid"">';
    html += '<div class=""tech-stat ok""><span class=""stat-num"">' + sN + '</span><span class=""stat-label"">Successful</span></div>';
    html += '<div class=""tech-stat warn""><span class=""stat-num"">' + wN + '</span><span class=""stat-label"">Warnings</span></div>';
    html += '<div class=""tech-stat fail""><span class=""stat-num"">' + fN + '</span><span class=""stat-label"">Failed</span></div>';
    html += '</div>';

    // Build sections by category from the DOM
    document.querySelectorAll('details.category').forEach(function(det) {
        var sumText = det.querySelector('summary').textContent.trim();
        var mdash = sumText.indexOf('\u2014');
        var cat = mdash > 0 ? sumText.substring(0, mdash).replace(/^[^\w]*/, '').trim() : sumText;
        var itemInfo = mdash > 0 ? sumText.substring(mdash + 1).trim() : '';
        var status = det.dataset.status;
        var statusClass = status === 'Success' ? 'status-ok' : (status === 'Failed' ? 'status-fail' : 'status-warn');
        var statusIcon = status === 'Success' ? '\u2713' : (status === 'Failed' ? '\u2717' : '\u26A0');

        // Collect items done in this category
        var items = [];

        // Get completed manual actions within this category
        var section = det.querySelector('.manual-section');
        if (section) {
            section.querySelectorAll('.action-item').forEach(function(item) {
                var cb = item.querySelector('.action-check');
                if (cb && cb.checked) {
                    var title = item.querySelector('strong');
                    if (title) items.push(title.textContent);
                }
            });
        }

        // Build the collapsible section
        var countStr = itemInfo.split(' ')[0] || '';
        var extraInfo = items.length > 0 ? ' + ' + items.length + ' manual' : '';
        html += '<details class=""tech-section"" open>';
        html += '<summary><span><span class=""' + statusClass + '"">' + statusIcon + '</span> ' + cat + '</span>';
        html += '<span class=""count"">' + countStr + ' auto-processed' + extraInfo + '</span></summary>';
        html += '<div class=""section-body"">';

        // Status row
        html += '<table><tr><th>Status</th><th>Items Processed</th></tr>';
        html += '<tr><td class=""' + statusClass + '"">' + status + '</td><td>' + countStr + '</td></tr>';
        html += '</table>';

        // Warnings if any
        var warnings = [];
        det.querySelectorAll('.warnings li').forEach(function(li) { warnings.push(li.textContent); });
        if (warnings.length > 0) {
            html += '<table><tr><th class=""status-warn"">Notes</th></tr>';
            warnings.forEach(function(w) { html += '<tr><td>' + w + '</td></tr>'; });
            html += '</table>';
        }

        // Completed manual items
        if (items.length > 0) {
            html += '<ul>';
            items.forEach(function(t) {
                html += '<li><span class=""check-icon"">\u2713</span>' + t + '</li>';
            });
            html += '</ul>';
        }

        html += '</div></details>';
    });

    // Standalone completed manual actions (not inside a category)
    var standaloneEl = document.getElementById('manual-actions');
    if (standaloneEl) {
        var standaloneItems = [];
        standaloneEl.querySelectorAll('.action-item').forEach(function(item) {
            var cb = item.querySelector('.action-check');
            if (cb && cb.checked) {
                var title = item.querySelector('strong');
                if (title) standaloneItems.push(title.textContent);
            }
        });
        if (standaloneItems.length > 0) {
            html += '<details class=""tech-section"" open>';
            html += '<summary><span><span class=""status-ok"">\u2713</span> Additional Manual Installations</span>';
            html += '<span class=""count"">' + standaloneItems.length + ' completed</span></summary>';
            html += '<div class=""section-body""><ul>';
            standaloneItems.forEach(function(t) {
                html += '<li><span class=""check-icon"">\u2713</span>' + t + '</li>';
            });
            html += '</ul></div></details>';
        }
    }

    document.getElementById('techContent').innerHTML = html;
    document.getElementById('techOverlay').style.display = '';
}

function printTechReport() {
    document.body.classList.add('printing-tech');
    window.print();
    document.body.classList.remove('printing-tech');
}

function copyTechReport() {
    var lines = [];
    lines.push('WORKSTATION MIGRATION - COMPLETION REPORT');
    lines.push('ViperMigrate Tech Summary');
    lines.push('='.repeat(50));
    lines.push('');

    var metaEls = document.querySelectorAll('.header .meta');
    metaEls.forEach(function(el) { lines.push(el.textContent.trim()); });
    lines.push('Completed: ' + new Date().toLocaleString());
    lines.push('');

    var cards = document.querySelectorAll('.card .num');
    lines.push('Summary: ' + (cards[0]?cards[0].textContent:'0') + ' Successful, ' + (cards[1]?cards[1].textContent:'0') + ' Warnings, ' + (cards[2]?cards[2].textContent:'0') + ' Failed');
    lines.push('');

    document.querySelectorAll('details.category').forEach(function(det) {
        var sumText = det.querySelector('summary').textContent.trim();
        var mdash = sumText.indexOf('\u2014');
        var cat = mdash > 0 ? sumText.substring(0, mdash).replace(/^[^\w]*/, '').trim() : sumText;
        var itemInfo = mdash > 0 ? sumText.substring(mdash + 1).trim() : '';
        var status = det.dataset.status;

        lines.push(cat.toUpperCase() + ' - ' + status + ' - ' + itemInfo.split(' ')[0]);
        lines.push('-'.repeat(50));

        // Warnings
        det.querySelectorAll('.warnings li').forEach(function(li) {
            lines.push('  Note: ' + li.textContent);
        });

        // Completed manual items
        var section = det.querySelector('.manual-section');
        if (section) {
            section.querySelectorAll('.action-item').forEach(function(item) {
                var cb = item.querySelector('.action-check');
                if (cb && cb.checked) {
                    var title = item.querySelector('strong');
                    if (title) lines.push('  [x] ' + title.textContent);
                }
            });
        }
        lines.push('');
    });

    // Standalone completed
    var standaloneEl = document.getElementById('manual-actions');
    if (standaloneEl) {
        var hasAny = false;
        standaloneEl.querySelectorAll('.action-item').forEach(function(item) {
            var cb = item.querySelector('.action-check');
            if (cb && cb.checked) {
                if (!hasAny) {
                    lines.push('ADDITIONAL MANUAL INSTALLATIONS');
                    lines.push('-'.repeat(50));
                    hasAny = true;
                }
                var title = item.querySelector('strong');
                if (title) lines.push('  [x] ' + title.textContent);
            }
        });
        if (hasAny) lines.push('');
    }

    navigator.clipboard.writeText(lines.join('\n')).then(function() {
        var btns = document.querySelectorAll('.tech-toolbar .copy-btn');
        btns.forEach(function(b) {
            if (b.textContent === 'Copy to Clipboard') {
                var orig = b.textContent;
                b.textContent = 'Copied!';
                setTimeout(function() { b.textContent = orig; }, 1500);
            }
        });
    });
}

function closeTechReport() {
    document.getElementById('techOverlay').style.display = 'none';
}

function closeTechReportOnBg(e) {
    if (e.target === document.getElementById('techOverlay')) closeTechReport();
}
");
    }

    private static string E(string? s) => HttpUtility.HtmlEncode(s ?? "");

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n");
}
