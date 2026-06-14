/**
 * GL Journal Posted KPI Widget
 * Purpose  : Display the percentage of posted documents across
 *            GL_Journal and GL_JournalBatch.
 * Tables   : GL_Journal, GL_JournalBatch
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

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
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
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
                + '<div class="kpi-value success" id="VAS-gljp-val-' + id + '">'
                +   '<span class="VAS-gljp-num">—</span>'
                +   '<span class="VAS-gljp-pct">%</span>'
                + '</div>'
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-text">' + lbl('VAS_039_PostedDocuments', 'Posted documents.') + '</span>'
                + '</div>'
                + '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-gljp-val-' + id);
        }

        function loadData() {
            showBusy(true);
            $.ajax({
                url      : baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetPostedPercentage',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $kpiValue.find('.VAS-gljp-num').text(typeof data.Percentage === 'number' ? data.Percentage : 0);
                        } else {
                            $kpiValue.find('.VAS-gljp-num').text(0);
                        }
                    } catch (e) {
                        $kpiValue.find('.VAS-gljp-num').text('—');
                    }
                    showBusy(false);
                },
                error: function () {
                    $kpiValue.find('.VAS-gljp-num').text('—');
                    showBusy(false);
                }
            });
        }

        this.refreshWidget    = function () { $kpiValue.find('.VAS-gljp-num').text('—'); loadData(); };
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
    };

    VAS.VAS_039_GLJournalPostedWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_039_GLJournalPostedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
