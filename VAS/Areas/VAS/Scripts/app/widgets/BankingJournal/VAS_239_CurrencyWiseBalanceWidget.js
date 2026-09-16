/************************************************************
 * Module Name    : VAS
 * Purpose        : Currency-wise Balance - a 4x2 grid widget for the Banking
 *                  dashboard.
 *
 *                  What the tenant holds in each bank-account currency, and what that
 *                  is worth in the primary accounting schema's currency:
 *
 *                    (o) Currency-wise Balance
 *                        Balances summarized by currency
 *
 *                    Currency        Native        In INR (base)
 *                    [₹] INR         ₹3.96 Cr           ₹3.96 Cr
 *                    [$] USD         $84,200            ₹70.2 L
 *                    [€] EUR         €12,450            ₹11.3 L
 *                    [£] GBP         £4,900              ₹5.2 L
 *                    ─────────────────────────────────────────────
 *                    Total                              ₹4.82 Cr
 *
 *                  TWO COLUMNS OF MONEY, TWO DIFFERENT CURRENCIES, AND THE HEADER SAYS
 *                  WHICH. Native is in the ROW's own currency with that currency's
 *                  symbol and StdPrecision. The base column and the Total are in the
 *                  TENANT's reporting currency, whose ISO code is written into the
 *                  column header - never assumed, never hard-coded, and read from
 *                  AD_ClientInfo.C_AcctSchema1_ID server-side.
 *
 *                  THE TOTAL ROW'S NATIVE CELL IS DELIBERATELY EMPTY. Adding rupees to
 *                  dollars produces a number with no meaning, so the only total on this
 *                  card is the base-currency one. When any balance could not be
 *                  converted the total is withheld entirely rather than shown as a
 *                  partial figure that reads as complete.
 *
 *                  A MISSING EXCHANGE RATE IS NOT A ZERO. Such a row keeps its Native
 *                  amount and shows a dash in the base column, and the card says so -
 *                  a fabricated zero would understate the holding and the total alike.
 *
 *                  Design: design.md -> dashboard-widgets.md (Glass Widget, Widget
 *                  Header, Grid Data Widget, Row And Header Separators, Column Sizing
 *                  And Truncation, Widget Footer Pager, Content Fit Budget, No Inner
 *                  Scrollbars) supplies the shell, the row grid and the pager; the
 *                  supplied widget.html supplies the three columns and the Total row.
 *
 *                  Two deliberate departures from that mock, both to keep it honest
 *                  with live data:
 *                    - the per-row flag swatch is a CURRENCY SYMBOL chip. The mock's
 *                      flags were hard-coded country colours; a colour per currency is
 *                      data this system does not hold, and design.md forbids a
 *                      different badge style per row. The chip carries C_Currency's own
 *                      symbol in one shared style instead, so it says something true.
 *                    - the base column header names the ISO code ("In INR (base)")
 *                      rather than the symbol, because one symbol can belong to several
 *                      currencies and this header is what tells the reader which one
 *                      the money is stated in.
 *
 *                  Summary Message Table
 *                  Rows marked (reuse) already exist under another key and are NOT
 *                  duplicated here.
 *                   # | Current Text                  | Message Key
 *                  ---+-------------------------------+-----------------------------
 *                   1 | Currency-wise Balance         | VAS_239_CurrencyWiseBalance
 *                   2 | Balances summarized by        | VAS_239_CurrencyWiseHint
 *                     |   currency                    |
 *                   3 | Currency                      | VAS_239_Currency
 *                   4 | Native                        | VAS_239_Native
 *                   5 | In                            | VAS_239_In
 *                   6 | (base)                        | VAS_239_BaseSuffix
 *                   7 | No bank balance data          | VAS_239_NoBalanceData
 *                     |   available                   |
 *                   8 | Rate unavailable              | VAS_239_RateUnavailable
 *                   9 | Total unavailable - some      | VAS_239_TotalUnavailable
 *                     |   exchange rates are missing  |
 *                  10 | Accounting schema not         | VAS_239_NoAcctSchema
 *                     |   configured                  |
 *                  11 | accounts                      | VAS_239_Accounts
 *                  12 | Balance as of                 | VAS_239_AsOf
 *                  13 | base currency                 | VAS_239_BaseCurrencyNote
 *                  14 | Total                         | VAS_Total           (reuse)
 *                  15 | Showing                       | VAS_020_Showing     (reuse)
 *                  16 | of                            | VAS_020_Of          (reuse)
 *                  17 | Previous                      | VAS_020_Prev        (reuse)
 *                  18 | Next                          | VAS_020_Next        (reuse)
 *                  19 | Couldn't load                 | VAS_192_CouldntLoad (reuse)
 *
 * Chronological development:
 *   VAI154         Created  Date 2026-09-04
 ***********************************************************/
; VAS = window.VAS || {};

; (function (VAS, $) {

    /* CSS lives in VAS/Areas/VAS/Content/VAS_239_CurrencyWiseBalanceWidget.css. All
       classes are namespaced `vas-239-` so they never collide with sibling widgets. */

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on :root
       equal to the dashboard container's current pixel width so the header clamps
       resolve against the dashboard's visible content area, not the viewport. One
       document-level observer serves every widget. */
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

    /* Inline SVG, not an icon-font class - the host shell does not always load an icon
       font and a missing glyph leaves an empty box. Explicit width/height as well as a
       viewBox: an SVG with only a viewBox falls back to 300x150px if a stylesheet is
       stale, which would sprawl across the header. */
    var ICONS = {
        globe: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<circle cx="12" cy="12" r="9"></circle>' +
            '<path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"></path></svg>',
        prev: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="15 18 9 12 15 6"></polyline></svg>',
        next: '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
            '<polyline points="9 18 15 12 9 6"></polyline></svg>'
    };

    var DEFAULT_PAGE_SIZE = 5;
    var MIN_PAGE_SIZE = 1;
    var MAX_PAGE_SIZE = 10;
    var ROW_HEIGHT_FALLBACK = 38;

    /* An unconvertible amount prints as a dash. It is never a zero: a zero is a real
       balance and would be read as one. */
    var NIL = '—';

    VAS.VAS_239_CurrencyWiseBalanceWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;

        var $self = this;
        var $root;
        var $card;
        var $list;
        var $total;
        var $foot;
        var $state;
        var $busy;

        var widgetID = 0;

        /* Unique event namespace per instance - a widget can sit twice on one dashboard. */
        var _ns = '';

        var _rows = [];
        var _base = null;
        var _asOf = '';
        var _page = 1;
        var _pageSize = DEFAULT_PAGE_SIZE;
        var _totalRows = 0;
        var _totalPages = 0;
        var _totalBase = 0;
        var _totalAvailable = false;
        var _hasConversionError = false;

        var _rowH = 0;
        var _needsSync = false;
        var _loading = false;
        var _disposed = false;
        var _rootObserver = null;
        var _listObserver = null;

        /* Monotonic request id. Only the newest read may paint - a page turn answered
           after a later refresh must not overwrite the newer figures. */
        var _reqSeq = 0;

        /* ------------------------------------------------------------ */
        /* Lifecycle                                                    */
        /* ------------------------------------------------------------ */
        this.initalize = function () {
            widgetID = (VIS.Utility && VIS.Utility.Util
                ? VIS.Utility.Util.getValueOfInt($self.widgetInfo.AD_UserHomeWidgetID)
                : 0);
            if (widgetID === 0) { widgetID = $self.windowNo; }
            _ns = '.vas239_' + widgetID;

            buildSkeleton();
            createBusyIndicator();
            setupRootObserver();
        };

        /* The framework's own widget loader, overlaid on the whole card while a read is in
           flight - the same treatment every sibling VAS widget gives its loads. It covers
           EVERY read: the initial load, the Refresh button and a page turn. Created visible
           so it is already up from the moment the widget mounts, which is also why no
           skeleton row is drawn: the card never shows a stale or mocked amount. */
        function createBusyIndicator() {
            $busy = $('<div class="vis-busyindicatorouterwrap vas-widget-busy-wrap">' +
                '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
            '</div>');
            $busy[0].style.visibility = 'visible';
            $root.append($busy);
        }

        function showBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'visible'; }
        }

        function hideBusyIndicator() {
            if ($busy && $busy[0]) { $busy[0].style.visibility = 'hidden'; }
        }

        /* Publishes THIS widget's own pixel width as --widget-inline-size on its root,
           which is the first variable the card's font-size clamp reads (the dashboard
           width is only the fallback). Every sibling widget does exactly this - without it
           the card measures itself against the whole dashboard and renders a size larger
           than its neighbours. */
        function setupRootObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                _rootObserver = new ResizeObserver(function (entries) {
                    if (!$root || !$root[0]) { return; }

                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                _rootObserver.observe($root[0]);
            } catch (e) { /* the clamp falls back to --dash-inline-size */ }
        }

        this.intialLoad = function () {
            fetchPage(1);
        };

        /* The dashboard's Refresh button calls this. It goes back to page 1: a refresh
           re-reads the whole set, a currency may have appeared or gone since the last load,
           and the page the user was on is not reliably the same page afterwards. */
        this.refreshWidget = function () {
            fetchPage(1);
        };

        /* ------------------------------------------------------------ */
        /* DOM skeleton                                                 */
        /* ------------------------------------------------------------ */
        function buildSkeleton() {
            $root = $('<div class="vas-239-root" id="vas-239-root-' + widgetID + '"></div>');

            var title = label('VAS_239_CurrencyWiseBalance', 'Currency-wise Balance');
            var subtitle = label('VAS_239_CurrencyWiseHint', 'Balances summarized by currency');

            $card = $(
                '<div class="vas-239-card">' +
                    '<div class="vas-239-header">' +
                        '<span class="vas-239-icon">' + ICONS.globe + '</span>' +
                        '<div class="vas-239-head-text">' +
                            '<div class="vas-239-title"></div>' +
                            '<div class="vas-239-subtitle"></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-239-body" role="table">' +
                        '<div class="vas-239-ghead vas-239-row" role="row"></div>' +
                        '<div class="vas-239-list" role="rowgroup"></div>' +
                        '<div class="vas-239-totalwrap" role="rowgroup"></div>' +
                        '<div class="vas-239-pagerwrap"></div>' +
                    '</div>' +
                    '<div class="vas-239-state vas-239-hidden"></div>' +
                '</div>'
            );

            $card.find('.vas-239-title').text(title).attr('title', title);
            $card.find('.vas-239-subtitle').text(subtitle).attr('title', subtitle);
            $card.find('.vas-239-body').attr('aria-label', title);

            $list = $card.find('.vas-239-list');
            $total = $card.find('.vas-239-totalwrap');
            $foot = $card.find('.vas-239-pagerwrap');
            $state = $card.find('.vas-239-state');

            paintHead();

            $root.append($card);
        }

        /* ------------------------------------------------------------ */
        /* Data                                                         */
        /* ------------------------------------------------------------ */
        function fetchPage(pageNo) {
            if (_loading) { return; }
            _loading = true;
            showBusyIndicator();

            _reqSeq++;
            var seq = _reqSeq;

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_239_CurrencyWiseBalanceWidget/GetCurrencyBalances',
                type: 'GET',
                dataType: 'json',
                /* Asynchronous, always - nothing here justifies blocking the UI thread. */
                async: true,
                data: { pageNo: pageNo, pageSize: _pageSize },
                success: function (raw) {
                    _loading = false;
                    if (_disposed || seq !== _reqSeq) { return; }
                    hideBusyIndicator();

                    var data = parseResponse(raw);
                    if (!data || data.error) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    /* No primary accounting schema means there is no base currency, so two
                       of the card's three columns have nothing to say. That is a
                       configuration state with its own message, not an empty list. */
                    if (data.NoAcctSchema) {
                        renderState(label('VAS_239_NoAcctSchema', 'Accounting schema not configured'));
                        return;
                    }

                    if (!data.Loaded) {
                        renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                        return;
                    }

                    _base = data.BaseCurrency || null;
                    _asOf = data.AsOfDate || '';
                    _rows = data.Rows || [];
                    _page = Number(data.Page) || 1;
                    _pageSize = Number(data.PageSize) || _pageSize;
                    _totalRows = Number(data.TotalRows) || 0;
                    _totalPages = Number(data.TotalPages) || 0;
                    _totalBase = Number(data.TotalBaseBalance) || 0;
                    _totalAvailable = data.TotalAvailable === true;
                    _hasConversionError = data.HasConversionError === true;

                    /* The base column's header names the tenant's own currency, so it is
                       repainted with every read rather than once at build time. */
                    paintHead();
                    paintRows();
                    paintTotal();
                    observeList();
                },
                error: function () {
                    _loading = false;
                    if (_disposed || seq !== _reqSeq) { return; }
                    /* The overlay comes down on failure too - a spinner left running over
                       an error the user cannot see is the worst of both. */
                    hideBusyIndicator();
                    renderState(label('VAS_192_CouldntLoad', "Couldn't load"));
                }
            });
        }

        /* The controller returns a JSON string inside a JSON response. */
        function parseResponse(raw) {
            try {
                return (typeof raw === 'string') ? (raw ? JSON.parse(raw) : null) : raw;
            }
            catch (e) { if (window.console) { console.log(e); } return null; }
        }

        /* ------------------------------------------------------------ */
        /* Render                                                       */
        /* ------------------------------------------------------------ */

        /* A load failure or a missing accounting schema takes the card over. Having no
           balance lines does NOT - that is handled inside the list, so the header and the
           column labels stay put. */
        function renderState(text) {
            $card.find('.vas-239-body').addClass('vas-239-hidden');
            $state.removeClass('vas-239-hidden').text(text);
        }

        /* §Grid Data Rows header: transparent background, Medium, muted, with a divider
           under it. The type scale goes on the CELLS, not on the row - the row sizes its
           tracks in em, and an em resolves against the element's OWN font-size, so a
           font-size here would compute the header's columns smaller than the body's and
           every label would sit over the wrong column.

           The third label names the base currency by ISO code - "In INR (base)" - because
           one symbol can belong to several currencies and this header is the only place
           the reader is told which one the right-hand column is stated in. */
        function paintHead() {
            var baseLabel = label('VAS_239_In', 'In') + ' ' + baseCode() + ' ' +
                label('VAS_239_BaseSuffix', '(base)');

            $card.find('.vas-239-ghead').html(
                '<span role="columnheader">' +
                    escapeHtml(label('VAS_239_Currency', 'Currency')) + '</span>' +
                '<span class="vas-239-num" role="columnheader">' +
                    escapeHtml(label('VAS_239_Native', 'Native')) + '</span>' +
                '<span class="vas-239-num" role="columnheader" title="' + escapeHtml(baseLabel) + '">' +
                    escapeHtml(baseLabel) + '</span>'
            );
        }

        function paintRows() {
            $state.addClass('vas-239-hidden');
            $card.find('.vas-239-body').removeClass('vas-239-hidden');

            if (!_rows || _rows.length === 0) {
                /* No balance lines at all - said inside the list, with no fabricated
                   zero-currency row anywhere, and with nothing to page or total. */
                $list.html('<div class="vas-239-empty">' +
                    escapeHtml(label('VAS_239_NoBalanceData', 'No bank balance data available')) + '</div>');
                $total.empty();
                $foot.empty();
                return;
            }

            var html = '';
            for (var i = 0; i < _rows.length; i++) { html += rowHtml(_rows[i]); }
            $list.html(html);

            paintFooter();

            /* Adapt the page size to the list height ONLY on the first paint / after a
               resize - never on manual navigation, which would flip pageSize under the
               user and re-clamp the page they just moved to. */
            if (_needsSync) { scheduleSync(); }
        }

        /* Currency, Native, Base. Every cell truncates and carries its own title, so a
           clipped figure is never a lost figure (§Column Sizing And Truncation). */
        function rowHtml(item) {
            var code = item.IsoCode || '';
            var symbol = item.Symbol || code;

            var native = amountText(item.NativeBalance, symbol, precisionOf(item.Precision), code);
            var nativeFull = fullAmountText(item.NativeBalance, symbol, precisionOf(item.Precision));

            /* A currency whose accounts did not all convert shows a dash rather than a
               partial sum, and says why on hover. */
            var available = item.BaseAvailable === true;
            var base = available
                ? amountText(item.BaseBalance, baseSymbol(), basePrecision(), baseCode())
                : NIL;
            var baseFull = available
                ? fullAmountText(item.BaseBalance, baseSymbol(), basePrecision())
                : label('VAS_239_RateUnavailable', 'Rate unavailable');

            var baseCls = available ? amountClass(item.BaseBalance) : ' vas-239-nil';

            return '<div class="vas-239-brow vas-239-row" role="row" ' +
                        'title="' + escapeHtml(rowTooltip(item)) + '">' +
                '<div class="vas-239-cur" role="cell">' +
                    '<span class="vas-239-sym" aria-hidden="true">' + escapeHtml(symbol) + '</span>' +
                    '<span class="vas-239-code" title="' + escapeHtml(code) + '">' +
                        escapeHtml(code) + '</span>' +
                '</div>' +
                '<div class="vas-239-amt vas-239-num' + amountClass(item.NativeBalance) + '" role="cell" ' +
                        'title="' + escapeHtml(nativeFull) + '">' + escapeHtml(native) + '</div>' +
                '<div class="vas-239-amt vas-239-num' + baseCls + '" role="cell" ' +
                        'title="' + escapeHtml(baseFull) + '">' + escapeHtml(base) + '</div>' +
            '</div>';
        }

        /* Everything the row holds, at full precision - the cells above are compact. */
        function rowTooltip(item) {
            var lines = [];
            var code = item.IsoCode || '';
            var symbol = item.Symbol || code;

            lines.push(code + ' · ' + (Number(item.BankAccountCount) || 0) + ' ' +
                label('VAS_239_Accounts', 'accounts'));

            lines.push(label('VAS_239_Native', 'Native') + ': ' +
                fullAmountText(item.NativeBalance, symbol, precisionOf(item.Precision)));

            if (item.BaseAvailable === true) {
                lines.push(label('VAS_239_In', 'In') + ' ' + baseCode() + ': ' +
                    fullAmountText(item.BaseBalance, baseSymbol(), basePrecision()));
            }
            else {
                lines.push(label('VAS_239_RateUnavailable', 'Rate unavailable'));
            }

            if (item.IsBaseCurrency === true) {
                lines.push(label('VAS_239_BaseCurrencyNote', 'base currency'));
            }

            if (_asOf) {
                lines.push(label('VAS_239_AsOf', 'Balance as of') + ' ' + formatDate(_asOf));
            }

            return lines.join('\n');
        }

        /* The Total row. Its NATIVE cell is empty on purpose - amounts in different
           currencies cannot be added - and its base cell is withheld as a dash whenever any
           balance failed to convert, because a total missing a currency still reads as a
           complete one. */
        function paintTotal() {
            if (!_rows || _rows.length === 0) { $total.empty(); return; }

            var totalLabel = label('VAS_Total', 'Total');

            var value = _totalAvailable
                ? amountText(_totalBase, baseSymbol(), basePrecision(), baseCode())
                : NIL;
            var full = _totalAvailable
                ? fullAmountText(_totalBase, baseSymbol(), basePrecision())
                : label('VAS_239_TotalUnavailable',
                    'Total unavailable - some exchange rates are missing');

            var cls = _totalAvailable ? '' : ' vas-239-nil';

            $total.html(
                '<div class="vas-239-trow vas-239-row" role="row">' +
                    '<span class="vas-239-tlabel" role="cell" title="' + escapeHtml(totalLabel) + '">' +
                        escapeHtml(totalLabel) + '</span>' +
                    /* Empty by design - see the note above. */
                    '<span class="vas-239-num" role="cell"></span>' +
                    '<span class="vas-239-tvalue vas-239-num' + cls + '" role="cell" ' +
                            'title="' + escapeHtml(full) + '">' + escapeHtml(value) + '</span>' +
                '</div>'
            );
        }

        /* ---- Canonical Widget Footer Pager (design.md): "Showing a–b of N" left,
             compact prev / next control right. Hidden on a single page. ---- */
        function paintFooter() {
            if (_totalPages <= 1) { $foot.empty(); return; }

            var from = (_page - 1) * _pageSize + 1;
            var to = Math.min(_page * _pageSize, _totalRows);

            var showing = label('VAS_020_Showing', 'Showing') + ' ' + from + '–' + to + ' ' +
                label('VAS_020_Of', 'of') + ' ' + _totalRows;

            var prevDis = _page <= 1 ? ' disabled' : '';
            var nextDis = _page >= _totalPages ? ' disabled' : '';

            $foot.html(
                '<div class="vas-239-pager">' +
                    '<span class="vas-239-pager-info">' + escapeHtml(showing) + '</span>' +
                    '<div class="vas-239-pager-nav">' +
                        '<button type="button" class="vas-239-pgbtn vas-239-pg-prev" aria-label="' +
                            escapeHtml(label('VAS_020_Prev', 'Previous')) + '"' + prevDis + '>' + ICONS.prev + '</button>' +
                        '<span class="vas-239-pager-label">' + _page + ' ' +
                            escapeHtml(label('VAS_020_Of', 'of')) + ' ' + _totalPages + '</span>' +
                        '<button type="button" class="vas-239-pgbtn vas-239-pg-next" aria-label="' +
                            escapeHtml(label('VAS_020_Next', 'Next')) + '"' + nextDis + '>' + ICONS.next + '</button>' +
                    '</div>' +
                '</div>'
            );

            $foot.find('.vas-239-pg-prev').on('click', function () {
                if (!_loading && _page > 1) { fetchPage(_page - 1); }
            });
            $foot.find('.vas-239-pg-next').on('click', function () {
                if (!_loading && _page < _totalPages) { fetchPage(_page + 1); }
            });
        }

        /* ------------------------------------------------------------ */
        /* Adaptive row capacity (the VAS_020 pattern)                  */
        /* ------------------------------------------------------------ */
        function scheduleSync() {
            var raf = window.requestAnimationFrame || function (cb) { return window.setTimeout(cb, 16); };
            raf(function () { syncCapacity(); });
        }

        function syncCapacity() {
            if (_disposed || _loading || !$list || !$list[0]) { return; }
            if (!_rows || _rows.length === 0) { return; }

            var avail = $list[0].clientHeight;
            if (avail <= 0) {
                /* Layout has not settled yet - try again on the next frame. */
                if (_needsSync) { scheduleSync(); }
                return;
            }

            /* Size off the TALLEST rendered row so nothing clips. */
            var rendered = $list[0].querySelectorAll('.vas-239-brow');
            var maxH = 0;
            for (var i = 0; i < rendered.length; i++) {
                if (rendered[i].offsetHeight > maxH) { maxH = rendered[i].offsetHeight; }
            }
            if (maxH > 0) { _rowH = maxH; }
            var rowH = _rowH > 0 ? _rowH : ROW_HEIGHT_FALLBACK;

            _needsSync = false;

            var capacity = Math.floor(avail / rowH);
            if (capacity < MIN_PAGE_SIZE) { capacity = MIN_PAGE_SIZE; }
            if (capacity > MAX_PAGE_SIZE) { capacity = MAX_PAGE_SIZE; }

            if (capacity !== _pageSize) {
                _pageSize = capacity;
                fetchPage(_page);
            }
        }

        function observeList() {
            if (typeof ResizeObserver === 'undefined' || !$list || !$list[0]) { return; }
            if (_listObserver) { try { _listObserver.disconnect(); } catch (e) { /* ignore */ } }

            _listObserver = new ResizeObserver(function () {
                if (_disposed || _loading) { return; }
                _needsSync = true;
                syncCapacity();
            });
            _listObserver.observe($list[0]);
        }

        /* ------------------------------------------------------------ */
        /* Base currency accessors                                      */
        /* ------------------------------------------------------------ */
        function baseCode() {
            return (_base && _base.IsoCode) ? _base.IsoCode : '';
        }

        function baseSymbol() {
            if (!_base) { return ''; }
            return _base.Symbol ? _base.Symbol : (_base.IsoCode || '');
        }

        function basePrecision() {
            return precisionOf(_base ? _base.Precision : 2);
        }

        /* ------------------------------------------------------------ */
        /* Formatting - per COLUMN currency                             */
        /* ------------------------------------------------------------ */
        function precisionOf(value) {
            var p = Number(value);
            return (isNaN(p) || p < 0 || p > 6) ? 2 : p;
        }

        /* Compact magnitude with the symbol composed on, and a true minus sign rather than
           a hyphen for an overdrawn balance. The CURRENCY passed in drives the scale - the
           ISO code decides whether it steps in lakh/crore or million/billion - so the
           Native cell scales by the row's currency and the base cell by the tenant's, and
           no tenant currency is assumed anywhere.

           There is no explicit "+": these are balances, not movements, so a positive one
           needs no sign to be read correctly. */
        function amountText(value, symbol, precision, isoCode) {
            var v = Number(value) || 0;
            var magnitude;

            try {
                if (VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
                    magnitude = VIS.Util.formatCompactAmount(v, isoCode || '', precision);
                }
            }
            catch (e) { if (window.console) { console.log(e); } }

            if (magnitude === undefined) { magnitude = String(Math.abs(v)); }

            return (v < 0 ? '−' : '') + (symbol || '') + magnitude;
        }

        /* Full, non-compact amount for the tooltip: the exact figure behind the compact
           cell. Grouping and the decimal separator come from the browser locale, the
           decimals from the COLUMN currency's precision. */
        function fullAmountText(value, symbol, precision) {
            var v = Number(value) || 0;
            var text = Math.abs(v).toLocaleString(window.navigator.language,
                { minimumFractionDigits: precision, maximumFractionDigits: precision });

            return (v < 0 ? '−' : '') + (symbol || '') + text;
        }

        /* Only an overdrawn balance is tinted. A positive balance keeps the row's own dark
           text - tinting every figure green would make the exception invisible, and the
           printed minus already carries the meaning without the colour. */
        function amountClass(value) {
            return (Number(value) || 0) < 0 ? ' vas-239-neg' : '';
        }

        /* Dates arrive as yyyy-MM-dd and are rendered in the browser's locale. Parsed part
           by part rather than through Date(string), which reads a bare ISO date as UTC and
           shifts it a day back for anyone west of Greenwich. */
        function formatDate(isoDate) {
            if (!isoDate) { return ''; }

            var parts = String(isoDate).split('-');
            if (parts.length !== 3) { return String(isoDate); }

            var d = new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10));
            if (isNaN(d.getTime())) { return String(isoDate); }

            try {
                return d.toLocaleDateString(window.navigator.language,
                    { day: '2-digit', month: 'short', year: 'numeric' });
            }
            catch (e) { return String(isoDate); }
        }

        /* ------------------------------------------------------------ */
        /* Helpers                                                      */
        /* ------------------------------------------------------------ */

        /* Every database-sourced string - currency codes and symbols included - goes
           through here before it reaches the DOM. */
        function escapeHtml(s) {
            var v = (s === null || s === undefined) ? '' : String(s);
            return v.replace(/&/g, '&amp;').replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        /* Every user-facing string goes through AD_Message; the fallback keeps the card
           readable when a key has not been seeded yet. */
        function label(key, fallback) {
            try {
                if (VIS.Msg && typeof VIS.Msg.getMsg === 'function') {
                    var v = VIS.Msg.getMsg(key);
                    if (v && v !== key && v.charAt(0) !== '[') { return v; }
                }
            }
            catch (e) { /* ignore */ }
            return fallback;
        }

        this.getRoot = function () { return $root; };

        /* Release everything that outlives the card: both observers - a ResizeObserver left
           running keeps the whole subtree alive - and every handler bound inside it. */
        this.releasePanel = function () {
            _disposed = true;

            if (_rootObserver) {
                try { _rootObserver.disconnect(); } catch (e) { /* ignore */ }
                _rootObserver = null;
            }
            if (_listObserver) {
                try { _listObserver.disconnect(); } catch (e) { /* ignore */ }
                _listObserver = null;
            }

            if ($busy) { $busy.remove(); $busy = null; }
            if ($list) { $list.off(_ns); }
            if ($foot) { $foot.off(); }

            _rows = [];
            _base = null;
        };
    };

    /* ---------------------------------------------------------------- */
    /* Required prototype hooks (same surface as other VAS widgets)     */
    /* ---------------------------------------------------------------- */
    VAS.VAS_239_CurrencyWiseBalanceWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the header clamps read. */
        ensureDashInlineSizeVar(this.getRoot());

        this.intialLoad();
    };

    /* No prototype refreshWidget: the constructor already defines the instance method,
       which shadows anything on the prototype. A prototype version calling
       this.refreshWidget() would be unreachable at best and infinite recursion the day
       the instance one is removed. */

    VAS.VAS_239_CurrencyWiseBalanceWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_239_CurrencyWiseBalanceWidget.prototype.dispose = function () {
        try { this.releasePanel(); } catch (e) { /* ignore */ }
        if (this.frame && typeof this.frame.dispose === 'function') {
            try { this.frame.dispose(); } catch (e) { /* ignore */ }
        }
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
