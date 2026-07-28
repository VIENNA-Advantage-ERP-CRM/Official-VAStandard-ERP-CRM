/**
 * Backorders Widget (Delivery Order dashboard)
 * Widget number 142.
 * Widget size: 2 columns x 1 row.
 * Display-only KPI tile: count of active completed Sales Order lines whose
 * remaining quantity (QtyOrdered - QtyDelivered) exceeds the matching
 * on-hand stock (M_Storage.QtyOnHand summed across all active locators of
 * the order's warehouse, narrowed to the same Product + Attribute Set
 * Instance). Counts Sales Order lines, never Delivery Order headers/lines.
 * Read-only, not clickable, no modal. Plain DOM internals (no jQuery in the
 * widget logic); jQuery is used only at the framework boundary.
 * Backend - VAS_142_BackordersWidget/GetSummary
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | Backorders                            | VAS_142_BKO_Title
 *  2 | Stock shortfall                       | VAS_142_BKO_Meta
 *  3 | Data unavailable                      | VAS_142_BKO_DataUnavailable
 *  4 | sales order lines have a stock shortfall (count prepended in code, used as aria-label) | VAS_142_BKO_AriaSuffix
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

    VAS.VAS_142_BackordersWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-bko-root';

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

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-bko-card');
            card.setAttribute('aria-label', lbl('VAS_142_BKO_Title', 'Backorders'));
            card.appendChild(el('span', 'MPC-bko-title', lbl('VAS_142_BKO_Title', 'Backorders')));

            var body = el('div', 'MPC-bko-body');
            var value = el('div', 'MPC-bko-value MPC-bko-neutral', '…');
            var meta = el('div', 'MPC-bko-meta', lbl('VAS_142_BKO_Meta', 'Stock shortfall'));
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

            var url = VIS.Application.contextUrl + 'VAS_142_BackordersWidget/GetSummary';
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
            var count = data.backorderLineCount || 0;
            els.value.classList.remove('MPC-bko-neutral');
            els.value.textContent = String(count);
            els.meta.textContent = lbl('VAS_142_BKO_Meta', 'Stock shortfall');
            els.card.setAttribute('aria-label', count + ' ' + lbl('VAS_142_BKO_AriaSuffix', 'sales order lines have a stock shortfall'));
        }

        function showError() {
            els.value.classList.add('MPC-bko-neutral');
            els.value.textContent = '—';
            els.meta.textContent = lbl('VAS_142_BKO_DataUnavailable', 'Data unavailable');
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

    VAS.VAS_142_BackordersWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_142_BackordersWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_142_BackordersWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_142_BackordersWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_142_BackordersWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_142_BackordersWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
