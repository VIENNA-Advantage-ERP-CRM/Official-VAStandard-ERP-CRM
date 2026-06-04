/**
 * GL Journal Unposted KPI Widget
 * Purpose  : Display the count of GL Journal documents that are not yet posted
 *            (DocStatus IN 'DR','CO','CL' AND Posted = 'N').
 * Table    : GL_Journal
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_036_GLJUnposted, VAS_036_Why, VAS_036_DraftsWaiting
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    VAS.VAS_036_GLJournalUnpostedWidget = function () {

        this.frame;
        this.windowNo;
        var $self    = this;
        var $root    = $('<div class="VAS-glju-root">');
        var $kpiValue;
        var $whyText;
        var baseUrl  = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-glju-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-glju-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="4" y1="22" x2="4" y2="15"></line>'
                + '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"></path>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-amber">'
                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_036_GLJUnposted', 'Unposted') + '</div>'
                + '</div>'
                + '<div class="kpi-value warning" id="VAS-glju-val-' + id + '">—</div>'
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-label">' + lbl('VAS_036_Why', 'Why') + '</span>'
                +   '<span class="kpi-why-text" id="VAS-glju-why-' + id + '">'
                +     lbl('VAS_036_DraftsWaiting', 'Drafts waiting to be approved + posted.')
                +   '</span>'
                + '</div>'
                + '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-glju-val-' + id);
            $whyText  = $root.find('#VAS-glju-why-' + id);
        }

        function loadData() {
            showBusy(true);
            $.ajax({
                url      : baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedCount',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $kpiValue.text(typeof data.UnpostedCount === 'number' ? data.UnpostedCount : 0);
                        } else {
                            $kpiValue.text(0);
                            $whyText.text(lbl('VIS_NoData', 'No data available.'));
                        }
                    } catch (e) {
                        $kpiValue.text('—');
                        $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    }
                    showBusy(false);
                },
                error: function () {
                    $kpiValue.text('—');
                    $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    showBusy(false);
                }
            });
        }

        this.refreshWidget    = function () { $kpiValue.text('—'); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
