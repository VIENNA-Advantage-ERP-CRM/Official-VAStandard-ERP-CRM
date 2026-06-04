/**
 * GL Journal Entries KPI Widget
 * Purpose  : Display the count of actual GL Journal documents (PostingType='A')
 *            posted in the current calendar month.
 * Table    : GL_Journal
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_041_GLJEntries, VAS_041_Why, VAS_041_AllJournalEntries
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    VAS.VAS_041_GLJournalEntriesWidget = function () {

        this.frame;
        this.windowNo;
        var $self    = this;
        var $root    = $('<div class="VAS-glje-root">');
        var $kpiValue;
        var $whyText;
        var $titleEl;
        var baseUrl  = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-glje-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-glje-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>'
                + '<polyline points="14 2 14 8 20 8"></polyline>'
                + '<path d="M16 13H8M16 17H8"></path>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-blue">'
                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title" id="VAS-glje-title-' + id + '">'
                +     lbl('VAS_041_GLJEntries', 'Entries') + ' &middot; &mdash;'
                +   '</div>'
                + '</div>'
                + '<div class="kpi-value" id="VAS-glje-val-' + id + '">—</div>'
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-label">' + lbl('VAS_041_Why', 'Why') + '</span>'
                +   '<span class="kpi-why-text" id="VAS-glje-why-' + id + '">&mdash;</span>'
                + '</div>'
                + '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-glje-val-'   + id);
            $whyText  = $root.find('#VAS-glje-why-'   + id);
            $titleEl  = $root.find('#VAS-glje-title-' + id);
        }

        function loadData() {
            showBusy(true);
            $.ajax({
                url      : baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntryCount',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $titleEl.html(lbl('VAS_041_GLJEntries', 'Entries') + ' &middot; ' + (data.MonthAbbr || ''));
                            $kpiValue.text(typeof data.EntryCount === 'number' ? data.EntryCount : 0);
                            $whyText.text(lbl('VAS_041_AllJournalEntries', 'All journal entries posted in') + ' ' + (data.MonthName || '') + '.');
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

        this.refreshWidget = function () {
            $kpiValue.text('—');
            $whyText.text('—');
            loadData();
        };

        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_041_GLJournalEntriesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
