/**
 * Shipped Today Widget (Delivery Order dashboard)
 * Widget number 153.
 * Widget size: 2 columns x 1 row.
 * Display-only KPI tile. Main value: count of active customer Delivery Order
 * headers (MovementType 'C-', sales, non-return) in Completed (CO) or Closed
 * (CL) status whose MovementDate is today. Meta: number of partial Sales
 * Order lines included in those shipments (linked lines delivered today whose
 * cumulative QtyDelivered is still below QtyOrdered). Counts DO headers and
 * distinct Sales Order lines - never double-counts. Read-only, not clickable,
 * no modal. Plain DOM internals (no jQuery in the widget logic); jQuery is
 * used only at the framework boundary.
 * Backend - VAS_153_ShippedTodayWidget/GetSummary
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | Shipped Today                         | VAS_153_SHT_Title
 *  2 | partial (suffix after the count)      | VAS_153_SHT_MetaPartial
 *  3 | Data unavailable                      | VAS_153_SHT_DataUnavailable
 *  4 | delivery orders shipped today (count prepended in code, aria) | VAS_153_SHT_AriaOrders
 *  5 | partial sales order lines (count prepended in code, aria)     | VAS_153_SHT_AriaPartial
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar(rootEl) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined' || !rootEl || !rootEl.closest) { return; }

        var container = rootEl.closest('.vis-widget-container, [data-dashboard-container]');
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_153_ShippedTodayWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-sht-root';

        var els = {};
        var loadController = null;
        var loadToken = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        function metaText(partial) {
            return String(partial) + ' ' + lbl('VAS_153_SHT_MetaPartial', 'partial');
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-sht-card');
            card.setAttribute('aria-label', lbl('VAS_153_SHT_Title', 'Shipped Today'));
            card.appendChild(el('span', 'MPC-sht-title', lbl('VAS_153_SHT_Title', 'Shipped Today')));

            var body = el('div', 'MPC-sht-body');
            var value = el('div', 'MPC-sht-value MPC-sht-neutral', '…');
            var meta = el('div', 'MPC-sht-meta', metaText(0));
            body.appendChild(value);
            body.appendChild(meta);
            card.appendChild(body);

            root.appendChild(card);
            els.card = card;
            els.value = value;
            els.meta = meta;
        }

        /* ---- Data load ---- */
        function loadSummary() {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            loadController = (typeof AbortController !== 'undefined') ? new AbortController() : null;
            var myToken = ++loadToken;

            var url = VIS.Application.contextUrl + 'VAS_153_ShippedTodayWidget/GetSummary';
            fetch(url, {
                method: 'GET',
                credentials: 'same-origin',
                signal: loadController ? loadController.signal : undefined
            }).then(function (res) { return res.text(); }).then(function (text) {
                if (myToken !== loadToken) { return; } // stale response
                var data = parseResponse(text);
                if (!data || data.error) { showError(); return; }
                showSummary(data);
            }).catch(function (err) {
                if (myToken !== loadToken) { return; }
                if (err && err.name === 'AbortError') { return; }
                showError();
            });
        }

        function parseResponse(text) {
            var data = text;
            try {
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
                if (typeof data === 'string' && data.length) { data = JSON.parse(data); }
            } catch (e) { return null; }
            return data || {};
        }

        function showSummary(data) {
            var shipped = data.shippedTodayCount || 0;
            var partial = data.partialLineCount || 0;
            els.value.classList.remove('MPC-sht-neutral');
            els.value.textContent = String(shipped);
            els.meta.textContent = metaText(partial);
            els.card.setAttribute('aria-label',
                shipped + ' ' + lbl('VAS_153_SHT_AriaOrders', 'delivery orders shipped today') + ', ' +
                partial + ' ' + lbl('VAS_153_SHT_AriaPartial', 'partial sales order lines'));
        }

        function showError() {
            els.value.classList.add('MPC-sht-neutral');
            els.value.textContent = '--';
            els.meta.textContent = '-- ' + lbl('VAS_153_SHT_MetaPartial', 'partial');
        }

        /* ---- Lifecycle ---- */
        this.Initalize = function () {
            build();
            loadSummary();
        };

        this.refreshWidget = function () {
            loadSummary();
        };

        this.getRoot = function () { return root; };

        this.disposeComponent = function () {
            if (loadController && typeof loadController.abort === 'function') {
                try { loadController.abort(); } catch (ignored) { }
            }
            if (root.parentNode) { root.parentNode.removeChild(root); }
        };
    };

    VAS.VAS_153_ShippedTodayWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_153_ShippedTodayWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_153_ShippedTodayWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_153_ShippedTodayWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_153_ShippedTodayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_153_ShippedTodayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
