/**
 * VAS_047_TodayCashInCashJournalWidget
 * Purpose - Shows today's total cash-in amount from the Cash Journal
 *           as a KPI tile with a delta badge vs the 7-day rolling average
 *           and a receipt count.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+----------------------------------
 *  1  | Cash in                              | VAS_047_CashInTitle
 *  2  | Today                                | VAS_047_PeriodToday
 *  3  | vs 7-day avg                         | VAS_047_Vs7DayAvg
 *  4  | receipts                             | VAS_047_Receipts
 *  5  | Loading…                             | VAS_Loading
 *  6  | No data available                    | VAS_NoData
 *  7  | Could not load data                  | VAS_ErrorLoading
 *  8  | Session Expired                      | VIS_SessionExpired
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    /**
     * VAS_047_TodayCashInCashJournalWidget
     * KPI widget: Today Cash In — Cash Journal
     */
    VAS.VAS_047_TodayCashInCashJournalWidget = function () {

        /* ── Private references ─────────────────────────────────────── */
        var $self = this;
        var $root = null;

        /* ── Label helper ───────────────────────────────────────────── */
        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        /* ── Amount formatter ───────────────────────────────────────── */
        function formatCurrencyAmount(value, currencyISO) {
            var numericValue = Number(value || 0);
            var stdPrecision = 2;

            try {
                if (VIS && VIS.Env && VIS.Env.getCtx &&
                    typeof VIS.Env.getCtx().getStdPrecision === 'function') {
                    stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
                }
            } catch (e) {
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        /* ── Loading overlay ────────────────────────────────────────── */
        function showBusy(show) {
            var $b = $root.find('#VAS-047-cj-busy-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $b.addClass('is-visible');
            } else {
                $b.removeClass('is-visible');
            }
        }

        /* ── State overlay ──────────────────────────────────────────── */
        function showState(show, message) {
            var $s = $root.find('#VAS-047-cj-state-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $s.text(message || '').addClass('is-visible');
            } else {
                $s.removeClass('is-visible').text('');
            }
        }

        /* ── Build skeleton DOM ─────────────────────────────────────── */
        function buildDOM() {
            var svgUp = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true">'
                + '<polyline points="18 15 12 9 6 15"></polyline></svg>';

            var svgCash = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true">'
                + '<rect x="2" y="7" width="20" height="14" rx="2" ry="2"></rect>'
                + '<path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"></path>'
                + '</svg>';

            var uid = $self.AD_UserHomeWidgetID;

            var html = ''
                + '<div class="VAS-047-today-cash-in-cash-journal-card">'

                + '  <div class="VAS-047-today-cash-in-cash-journal-header">'
                + '    <div class="VAS-047-today-cash-in-cash-journal-title-wrap">'
                + '      <span class="VAS-047-today-cash-in-cash-journal-title"'
                + '            id="VAS-047-cj-title-' + uid + '"></span>'
                + '    </div>'
                + '    <span class="VAS-047-today-cash-in-cash-journal-period"'
                + '          id="VAS-047-cj-period-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-body">'
                + '    <span class="VAS-047-today-cash-in-cash-journal-value"'
                + '          id="VAS-047-cj-value-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-footer">'
                + '    <span class="VAS-047-today-cash-in-cash-journal-badge"'
                + '          id="VAS-047-cj-badge-' + uid + '">'
                + '      <span id="VAS-047-cj-badge-icon-' + uid + '">' + svgUp + '</span>'
                + '      <span id="VAS-047-cj-badge-pct-' + uid + '"></span>'
                + '    </span>'
                + '    <span class="VAS-047-today-cash-in-cash-journal-desc"'
                + '          id="VAS-047-cj-desc-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-busy"'
                + '       id="VAS-047-cj-busy-' + uid + '">'
                + '    <span id="VAS-047-cj-loading-text-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-state"'
                + '       id="VAS-047-cj-state-' + uid + '"></div>'

                + '</div>';

            $root.html(html);

            $root.find('#VAS-047-cj-title-' + uid)
                .text(lbl('VAS_047_CashInTitle', 'Cash in'));

            $root.find('#VAS-047-cj-period-' + uid)
                .text(lbl('VAS_047_PeriodToday', 'Today'));

            $root.find('#VAS-047-cj-loading-text-' + uid)
                .text(lbl('VAS_Loading', 'Loading\u2026'));
        }

        /* ── Render data into DOM ───────────────────────────────────── */
        function renderData(data) {
            var uid = $self.AD_UserHomeWidgetID;
            var mainMetric = Number(data.mainMetric || 0);
            var avgDailyAmount = Number(data.avgDailyAmount || 0);
            var deltaRaw = Number(data.deltaPercent || 0);
            var receiptCount = data.receiptCount || data.recordCount || 0;

            if (!data.deltaPercent && avgDailyAmount > 0) {
                deltaRaw = Math.round(((mainMetric - avgDailyAmount) / avgDailyAmount) * 100);
            }

            var formatted = formatCurrencyAmount(mainMetric, data.currencyISO);
            $root.find('#VAS-047-cj-value-' + uid).text(formatted);

            var isPositive = (deltaRaw >= 0);
            var absPct = Math.abs(deltaRaw);
            var signPrefix = isPositive ? '+' : '-';
            var deltaText = signPrefix + absPct + '%';

            var $badge = $root.find('#VAS-047-cj-badge-' + uid);
            $badge.toggleClass('is-down', !isPositive);

            $root.find('#VAS-047-cj-badge-pct-' + uid).text(deltaText);

            var svgUp = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true"><polyline points="18 15 12 9 6 15"></polyline></svg>';

            var svgDown = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true"><polyline points="6 9 12 15 18 9"></polyline></svg>';

            $root.find('#VAS-047-cj-badge-icon-' + uid).html(isPositive ? svgUp : svgDown);

            var vs7day = lbl('VAS_047_Vs7DayAvg', 'vs 7-day avg');
            var rcptLbl = lbl('VAS_047_Receipts', 'receipts');
            var descText = vs7day + ' \u00B7 ' + receiptCount + ' ' + rcptLbl;

            $root.find('#VAS-047-cj-desc-' + uid).text(descText);
        }

        /* ── Load data from controller ──────────────────────────────── */
        function loadData() {
            showBusy(true);
            showState(false, '');

            var url = VIS.Application.contextUrl
                + 'VAS_047_TodayCashInCashJournalWidget/GetTodayCashInData';

            $.ajax({
                url: url,
                type: 'GET',
                dataType: 'json',
                success: function (response) {
                    if (!response || typeof response !== 'object') {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    if (response.error === 'Session Expired') {
                        showState(true, lbl('VIS_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (!response.success) {
                        var errMsg = response.error || lbl('VAS_ErrorLoading', 'Could not load data');
                        showState(true, errMsg);
                        return;
                    }

                    if (!response.hasData) {
                        showState(true, lbl('VAS_NoData', 'No data available'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        /* ── VIS widget lifecycle ───────────────────────────────────── */

        this.initalize = function ($el) {
            $self = this;

            if (!$self.AD_UserHomeWidgetID) {
                $self.AD_UserHomeWidgetID = $self.widgetInfo && $self.widgetInfo.AD_UserHomeWidgetID
                    ? $self.widgetInfo.AD_UserHomeWidgetID
                    : ($self.windowNo || new Date().getTime());
            }

            $root = $el ? $($el) : $('<div></div>');

            $root.addClass('VAS-047-today-cash-in-cash-journal-root');

            buildDOM();
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if ($root) {
                $root.off();
                $root.empty();
                $root = null;
            }
        };
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.widgetInfo = frame.widgetInfo;
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
