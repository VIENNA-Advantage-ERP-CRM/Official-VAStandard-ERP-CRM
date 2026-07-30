/**
 * GL Journal Posted KPI Widget
 * Purpose - Display the percentage of posted documents across GL journals.
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Posted                               | VAS_039_GLJPosted
 *  2  | Posted documents.                    | VAS_039_PostedDocuments
 *  3  | No Data                              | VIS_NoData
 *  4  | Error Loading Data                   | VIS_Error
 * ---------------------------------------------------------------------
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    /* Creates a single document-level ResizeObserver on the dashboard container
       and mirrors its width into the global CSS var --dash-inline-size (px), so
       the widget's clamp() sizing tracks the dashboard width, not the viewport. */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_039_GLJPosted, VAS_039_Why, VAS_039_PostedDocuments
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    VAS.VAS_039_GLJournalPostedWidget = function () {

        this.frame;
        this.windowNo;
        var $self   = this;
        var $root   = $('<div class="VAS-gljp-root">');
        var $kpiValue;
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljp-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljp-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<polyline points="20 6 9 17 4 12"></polyline>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-cream">'
                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_039_GLJPosted', 'Posted') + '</div>'
                + '</div>'
                + '<div class="kpi-value success" id="VAS-gljp-val-' + id + '">—</div>'
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-text">' + lbl('VAS_039_PostedDocuments', 'Posted documents.') + '</span>'
                + '</div>'
                + '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-gljp-val-' + id);
        }

        /* Always two decimals, with the user's locale decimal separator (87.50 % /
           87,50 %) — the same toLocaleString convention the amount widgets use. */
        function formatPercentage(value) {
            var number = Number(value);

            if (isNaN(number)) {
                number = 0;
            }

            return number.toLocaleString(
                window.navigator.language,
                {
                    minimumFractionDigits: 2,
                    maximumFractionDigits: 2
                }
            ) + '%';
        }

        function loadData() {
            showBusy(true);
            $.ajax({
                url      : baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetPostedPercentage',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    var data = result;

                    if (typeof data === 'string') {
                        try {
                            data = JSON.parse(data);
                        } catch (e) {
                            data = null;
                        }
                    }

                    var percentage = data ? Number(data.Percentage) : 0;

                    if (isNaN(percentage)) {
                        percentage = 0;
                    }

                    $kpiValue.text(formatPercentage(percentage));
                    showBusy(false);
                },
                error: function () {
                    $kpiValue.text('—');
                    showBusy(false);
                }
            });
        }

        this.refreshWidget    = function () { $kpiValue.text('—'); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_039_GLJournalPostedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_039_GLJournalPostedWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_039_GLJournalPostedWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_039_GLJournalPostedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
