/**
 * Out of Stock Widget (Product Master dashboard)
 * Widget number 132.
 * Widget size: 2 columns x 1 row.
 * Display-only KPI tile: count of active, stocked, non-discontinued products
 * with total on-hand quantity <= 0 (summed M_Storage.QtyOnHand across all
 * active storage rows, no reservation/allocation subtracted), plus how many
 * of those also appear on an open sales order line and how many appear on an
 * open purchase order line. No modal, no click, no drill-down in this scope -
 * built as a plain div so a future click-through can be added without
 * restructuring. Read-only. Plain DOM internals (no jQuery in the widget
 * logic); jQuery is used only at the framework boundary.
 * Backend - VAS_132_OutOfStockWidget/GetSummary
 * Summary Message Table
 *  # | Current Text                          | Message Key
 * ---+----------------------------------------+------------------------
 *  1 | Out of Stock                          | VAS_132_OOS_Title
 *  2 | {0} in open SOs · {1} in open POs     | VAS_132_OOS_Meta
 *  3 | Data unavailable                      | VAS_132_OOS_DataUnavailable
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

    VAS.VAS_132_OutOfStockWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var root = document.createElement('div');
        root.className = 'MPC-oos-root';

        var els = {};
        var loadController = null;
        var loadToken = 0;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function format(key, fallback) {
            var text = lbl(key, fallback);
            for (var i = 2; i < arguments.length; i++) {
                text = text.replace('{' + (i - 2) + '}', arguments[i]);
            }
            return text;
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) { node.className = className; }
            if (text != null) { node.textContent = text; }
            return node;
        }

        /* ---- Build the static DOM once ---- */
        function build() {
            var card = el('div', 'MPC-oos-card');
            card.appendChild(el('span', 'MPC-oos-title', lbl('VAS_132_OOS_Title', 'Out of Stock')));

            var body = el('div', 'MPC-oos-body');
            var value = el('div', 'MPC-oos-value MPC-oos-neutral', '…');
            var meta = el('div', 'MPC-oos-meta', '');
            body.appendChild(value);
            body.appendChild(meta);
            card.appendChild(body);

            root.appendChild(card);
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

            var url = VIS.Application.contextUrl + 'VAS_132_OutOfStockWidget/GetSummary';
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
            var outOfStock = data.outOfStock || 0;
            var inOpenSO = data.inOpenSO || 0;
            var inOpenPO = data.inOpenPO || 0;

            els.value.classList.remove('MPC-oos-neutral');
            els.value.textContent = String(outOfStock);
            els.meta.textContent = format('VAS_132_OOS_Meta', '{0} in open SOs · {1} in open POs', inOpenSO, inOpenPO);
        }

        function showError() {
            els.value.classList.add('MPC-oos-neutral');
            els.value.textContent = '—';
            els.meta.textContent = lbl('VAS_132_OOS_DataUnavailable', 'Data unavailable');
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

    VAS.VAS_132_OutOfStockWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_132_OutOfStockWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_132_OutOfStockWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_132_OutOfStockWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_132_OutOfStockWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_132_OutOfStockWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
