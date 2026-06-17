/************************************************************
 * Module Name    : VAS
 * Purpose        : Real-Time Alerts Widget
 * Created Date   : 14 May 2026
 * Created by     : Humam Yousif
 *
 * AD_Message keys needed (add via System Messages):
 *   VAS_021_RealTimeAlerts => "Real-time Alerts"
 *   VAS_021_NoAlerts       => "No alerts - all clear"
 *   VAS_021_Dismiss        => "Dismiss"
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    /* ---- Message helper: returns the AD_Message text, or the inline default
         when the system has no message for the key. ---- */
    function msg(key, fallback) {
        var value = VIS.Msg.getMsg(key);
        return value && value !== key && value !== '[' + key + ']' ? value : fallback;
    }

    VAS.VAS_021_RealTimeAlertsWidget = function () {
        this.frame;
        this.windowNo;
        var $bsyDiv;
        var $self = this;
        var $root = $('<div class="h-100 w-100 vas-widget-bg vas-rtawdg-root">');
        var $container;
        var widgetID = null;

        $self._kpiData = null;

        this.initalize = function () {
            widgetID = (VIS.Utility.Util.getValueOfInt(this.widgetInfo.AD_UserHomeWidgetID) !== 0
                ? this.widgetInfo.AD_UserHomeWidgetID
                : $self.windowNo);
            createBusyIndicator();
            buildShell();
            $bsyDiv[0].style.visibility = 'visible';
        };

        this.intialLoad = function () {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_021_RealTimeAlertsWidget/GetAlerts',
                dataType: 'json',
                async: true,
                success: function (res) {
                    var data = null;
                    try { data = (typeof res === 'string') ? JSON.parse(res) : res; } catch (e) { }
                    $self._kpiData = data;
                    renderWidget($self._kpiData);
                    $bsyDiv[0].style.visibility = 'hidden';
                },
                error: function () {
                    renderWidget(null);
                    $bsyDiv[0].style.visibility = 'hidden';
                }
            });
        };

        function buildShell() {
            $container = $('<div class="vas-rtawdg-container" id="vas_rtawdg_cont_' + widgetID + '">');
            $root.append($container);
        }

        function renderWidget(data) {
            $container.empty();

            var alerts = (data && data.Alerts) ? data.Alerts : [];
            var title = msg('VAS_021_RealTimeAlerts', 'Real-time Alerts');
            var badgeHtml = alerts.length > 0 ? '<span class="vas-rtawdg-badge">' + alerts.length + '</span>' : '';

            var html =
                '<div class="vas-rtawdg-header">' +
                    '<div class="vas-rtawdg-title">' +
                        '<svg class="vas-rtawdg-title-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"></circle>' +
                            '<line x1="12" y1="8" x2="12" y2="12"></line>' +
                            '<line x1="12" y1="16" x2="12.01" y2="16"></line>' +
                        '</svg>' +
                        rtaEsc(title) +
                    '</div>' +
                    badgeHtml +
                '</div>' +
                '<div class="vas-rtawdg-list">';

            if (alerts.length === 0) {
                html +=
                    '<div class="vas-rtawdg-empty">' +
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">' +
                            '<path d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"/>' +
                        '</svg>' +
                        (msg('VAS_021_NoAlerts', 'No alerts - all clear')) +
                    '</div>';
            } else {
                for (var i = 0; i < alerts.length; i++) {
                    html += rtaBuildRow(alerts[i], i);
                }
            }

            html += '</div>';
            $container.html(html);
        }

        function rtaBuildRow(alert, idx) {
            var typeClass = 'vas-rtawdg-al-' + (alert.Type || 'info');
            return (
                '<div class="vas-rtawdg-alert ' + typeClass + '" data-idx="' + idx + '">' +
                    '<div class="vas-rtawdg-al-icon">' + (alert.Icon || '!') + '</div>' +
                    '<div class="vas-rtawdg-al-body">' +
                        '<div class="vas-rtawdg-al-title">' + rtaEsc(alert.Title || '') + '</div>' +
                        '<div class="vas-rtawdg-al-sub">' + rtaEsc(alert.Subtitle || '') + '</div>' +
                    '</div>' +
                '</div>'
            );
        }

        function rtaEsc(str) {
            return String(str)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $bsyDiv[0].style.visibility = 'visible';
            $root.append($bsyDiv);
        }

        this.refreshWidget = function () {
            $self._kpiData = null;
            $bsyDiv[0].style.visibility = 'visible';
            $container.empty();
            $self.intialLoad();
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var self = this;
        window.setTimeout(function () { self.intialLoad(); }, 50);
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_021_RealTimeAlertsWidget.prototype.dispose = function () {
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
