/**
 * Top Customers by Collections Widget (VAS_013)
 * Purpose - Glass list widget ranking the top 5 customers by AR receipt
 *           collections over the last 30 days, in base/accounting currency.
 *           Each row shows the customer name (bold), the compact collection
 *           amount (e.g. ₹12.40L) and a proportion bar relative to the top
 *           customer (image_1). No modal / no pager — a fixed top-N list.
 * Design  - design.md "List Widget (FinanceListCard pattern)" + Glass Widget;
 *           bold md title + muted subtitle; row label/value = sm. All sizes in
 *           `em` per CLAUDE.md. Namespaced vas-tcr-*.
 *
 * Backend - VAS_013_TopCustomerReceipts/GetTopCustomers   (business logic in
 *           VASLogic.Models.VAS_013_TopCustomerReceiptsModel)
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                              | Message Key
 * ----+-------------------------------------------+----------------------------
 *  1  | Top customers by collections              | VAS_013_TopCustomerReceipts
 *  2  | This month                                | VAS_013_Subtitle
 *  3  | No collections in this period             | VAS_013_NoCollections
 * ─────────────────────────────────────────────────────────────────────
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    VAS.VAS_013_TopCustomerReceipts = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-tcr-root">');
        var $listEl;
        var $busy;

        /* How many customers to request from the backend. */
        var topN = 5;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function loadData() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_013_TopCustomerReceipts/GetTopCustomers',
                type: 'GET',
                cache: false,
                data: { topN: topN },
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (!data || data.error) { renderList({}, []); return; }

                    renderList(data, (data && data.Rows) ? data.Rows : []);
                },
                error: function () { renderList({}, []); },
                complete: function () { showBusy(false); }
            });
        }

        function getStdPrecision() {
            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    return VIS.Env.getCtx().getStdPrecision();
                }
            } catch (e) { /* fall through */ }
            return 2;
        }

        /* Compact-amount formatter: 10M→Cr (Indian crore), 100K→L (lakh),
           1K→K; below 1000 falls back to locale formatting at std precision. */
        function formatCompactAmount(value, stdPrecision) {
            value = Number(value || 0);

            if (value >= 10000000) {
                return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
            }
            if (value >= 100000) {
                return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
            }
            if (value >= 1000) {
                return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
            }

            var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
        }

        function formatExactAmount(value, stdPrecision) {
            var num = Number(value || 0);
            var prec = (typeof stdPrecision === "number") ? stdPrecision : getStdPrecision();
            return num.toLocaleString(window.navigator.language, {
                minimumFractionDigits: prec,
                maximumFractionDigits: prec
            });
        }

        /* Symbol *before* the amount, explicit sign before symbol (+ for zero or
           positive, - for negative). */
        function formatAmount(value, symbol, stdPrecision) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var compact = formatCompactAmount(Math.abs(value), stdPrecision);
            var sym = symbol ? '<span class="vas-tcr-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + sym + compact;
        }

        function renderList(data, rows) {
            if (!$listEl) { return; }
            $listEl.empty();

            if (!rows || rows.length === 0) {
                $listEl.removeClass('vas-tcr-list--fill');
                $listEl.html(
                    '<div class="vas-tcr-empty">' +
                    escapeHtml(lbl("VAS_013_NoCollections", "No collections in this period")) +
                    '</div>'
                );
                return;
            }

            /* Fill mode only when the list is full (all topN rows present):
               distribute the rows to fill the card so there is no dead space at
               the bottom. With fewer rows, stay top-anchored (fixed rhythm) so a
               couple of rows don't balloon apart. */
            $listEl.toggleClass('vas-tcr-list--fill', rows.length >= topN);

            var symbol = (data && data.CurrencySymbol) || "";
            var stdPrecision = Number((data && data.StdPrecision) || getStdPrecision());

            /* Rows arrive sorted highest-first, so the first row is the maximum
               and anchors the proportion bars. Guard against a zero / negative
               top value so widths stay finite. */
            var maxAmount = 0;
            for (var m = 0; m < rows.length; m++) {
                var v = Math.abs(Number(rows[m].Amount || 0));
                if (v > maxAmount) { maxAmount = v; }
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var name = row.CustomerName || "";
                var amount = Number(row.Amount || 0);
                var amtHtml = formatAmount(amount, symbol, stdPrecision);
                var exactSign = amount < 0 ? '-' : '';
                var exactTitle = exactSign + (symbol ? symbol + ' ' : '') + formatExactAmount(Math.abs(amount), stdPrecision);

                /* Proportion of the top customer; keep a small floor so tiny
                   non-zero values still render a visible sliver. */
                var pct = 0;
                if (maxAmount > 0) { pct = (Math.abs(amount) / maxAmount) * 100; }
                if (pct > 0 && pct < 4) { pct = 4; }
                if (pct > 100) { pct = 100; }

                var $rowEl = $(
                    '<div class="vas-tcr-row">' +
                    '<div class="vas-tcr-row-top">' +
                    '<span class="vas-tcr-name" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</span>' +
                    '<span class="vas-tcr-amount" title="' + escapeHtml(exactTitle) + '">' + amtHtml + '</span>' +
                    '</div>' +
                    '<div class="vas-tcr-bar"><div class="vas-tcr-bar-fill"></div></div>' +
                    '</div>'
                );

                /* Set the width via JS (the percentage is data-driven, not a
                   fixed design value, so it stays out of the CSS file). */
                $rowEl.find('.vas-tcr-bar-fill').css('width', pct + '%');

                $listEl.append($rowEl);
            }
        }

        /* Bar-chart glyph (matches image_1 header icon). */
        function barChartIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<line x1="6" y1="20" x2="6" y2="13"></line>' +
                '<line x1="12" y1="20" x2="12" y2="8"></line>' +
                '<line x1="18" y1="20" x2="18" y2="4"></line>' +
                '</svg>';
        }

        function createWidget() {
            var $card = $(
                '<div class="vas-tcr-card">' +

                '<div class="vas-tcr-head">' +
                '<div class="vas-tcr-icon">' + barChartIconSvg() + '</div>' +
                '<div class="vas-tcr-title-group">' +
                '<div class="vas-tcr-title">' + escapeHtml(lbl("VAS_013_TopCustomerReceipts", "Top customers by collections")) + '</div>' +
                '<div class="vas-tcr-subtitle">' + escapeHtml(lbl("VAS_013_Subtitle", "This month")) + '</div>' +
                '</div>' +
                '</div>' +

                '<div class="vas-tcr-list"></div>' +

                '</div>'
            );

            $listEl = $card.find('.vas-tcr-list');

            $root.append($card);

            $busy = $('<div class="vas-tcr-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_013_TopCustomerReceipts.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_013_TopCustomerReceipts.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_013_TopCustomerReceipts.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_013_TopCustomerReceipts.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
