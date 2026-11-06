/**
 * VAS_305_RequisitionSearchWidget - 9x1 search on the Requisition screen (and its Home copy).
 * Searches requisitions by number, product name / code / SKU, charge, requester, warehouse or
 * description. Results show in a popover under the bar (fixed, so the 1-row cell does not clip it);
 * choosing one opens the requisition popup. The search is local to this widget - it never
 * filters or reloads any other widget on the screen.
 * Backend: VAS_305_RequisitionSearchWidget/Search?query=&offset=&maxRows=
 *
 * Labels / Message Keys
 *  VAS_305_SearchPlaceholder  Search requisitions by number, product name or code, requester, warehouse
 *  VAS_305_Clear              Clear
 *  VAS_305_TypeMore           Type at least 2 characters
 *  VAS_305_Searching          Searching…
 *  VAS_305_NoMatch            No requisition matches
 *  VAS_305_Results            results
 *  VAS_305_ScrollMore         Scroll for more
 *  VAS_305_Lines / VAS_305_NeedBy / VAS_305_LoadError (shared)
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    var MIN_LEN = 2, PAGE = 15;

    VAS.VAS_305_RequisitionSearchWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $box, $input, $panel, items = [], total = 0, term = '', active = -1, timer = null, req = 0, loadingMore = false;
        // busy: a search is waiting or running; enterWhenReady: Enter was pressed before its results came back
        var busy = false, enterWhenReady = false;
        this.frame = null;
        this.windowNo = -1;

        function hl(text) {
            var t = U.esc(text || '');
            if (!term) { return t; }
            var safe = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            return t.replace(new RegExp('(' + U.esc(safe) + ')', 'ig'), '<mark>$1</mark>');
        }

        function position() {
            var r = $box[0].getBoundingClientRect();
            var top = r.bottom + 6, h = Math.max(160, Math.min(420, window.innerHeight - top - 16));
            $panel.css({ left: r.left, top: top, width: r.width, maxHeight: h });
        }

        function show(html) {
            $panel.html(html).addClass('on');
            position();
        }

        function hide() { $panel.removeClass('on'); active = -1; }

        function itemHtml(it, i) {
            return '<div class="it' + (i === active ? ' on' : '') + '" data-i="' + i + '">' +
                '<b>' + hl(it.documentNo) + '</b>' +
                '<span class="side">' + U.esc(it.docStatusName || '') + '<br>' + U.esc(U.lbl('VAS_305_NeedBy', 'Need by')) + ' ' + U.date(it.needBy) + '</span>' +
                '<span class="m1">' + hl(it.item || '') + (it.lines > 1 ? ' +' + (it.lines - 1) : '') + ' · ' + hl(it.requester || '') + ' · ' + hl(it.warehouse || '') + '</span></div>';
        }

        function render() {
            if (!items.length) {
                show('<div class="empty">' + U.esc(U.lbl('VAS_305_NoMatch', 'No requisition matches')) + ' “' + U.esc(term) + '”</div>');
                return;
            }
            show('<div class="hd">' + U.num(total) + ' ' + U.esc(U.unit(total, 'VAS_305_Results', 'results')) + '</div><div class="list">' +
                items.map(itemHtml).join('') + (items.length < total ? '<div class="more">' + U.esc(U.lbl('VAS_305_ScrollMore', 'Scroll for more')) + '</div>' : '') + '</div>');
            $panel.find('.list').on('scroll', function () {
                if (loadingMore || items.length >= total) { return; }
                if (this.scrollTop + this.clientHeight >= this.scrollHeight - 30) { loadMore(); }
            });
        }

        function run(q) {
            var mine = ++req;
            show('<div class="empty">' + U.esc(U.lbl('VAS_305_Searching', 'Searching…')) + '</div>');
            U.get('VAS_305_RequisitionSearchWidget/Search', { query: q, offset: 0, maxRows: PAGE }).done(function (d) {
                if (mine !== req) { return; }
                busy = false;
                items = d.items || []; total = d.total || 0; active = items.length ? 0 : -1;
                render();
                if (enterWhenReady) { enterWhenReady = false; choose(0); }
            }).fail(function (err) {
                if (mine !== req) { return; }
                busy = false; enterWhenReady = false;
                show('<div class="empty">' + U.esc(err) + '</div>');
            });
        }

        function loadMore() {
            loadingMore = true;
            var mine = req, keep = $panel.find('.list').scrollTop();
            U.get('VAS_305_RequisitionSearchWidget/Search', { query: term, offset: items.length, maxRows: PAGE }).done(function (d) {
                if (mine !== req) { return; }
                items = items.concat(d.items || []);
                render();
                $panel.find('.list').scrollTop(keep);
            }).always(function () { loadingMore = false; });
        }

        function choose(i) {
            var it = items[i];
            if (!it) { return; }
            hide();
            U.showRequisition(self, it.requisitionId);
        }

        function onInput() {
            term = $.trim($input.val());
            $box.toggleClass('has', !!$input.val());
            if (timer) { window.clearTimeout(timer); }
            enterWhenReady = false;
            if (term.length < MIN_LEN) {
                req++; busy = false;
                if (term.length) { show('<div class="empty">' + U.esc(U.lbl('VAS_305_TypeMore', 'Type at least 2 characters')) + '</div>'); }
                else { hide(); }
                return;
            }
            busy = true;
            timer = window.setTimeout(function () { run(term); }, 280);
        }

        this.Initalize = function () {
            var ph = U.lbl('VAS_305_SearchPlaceholder', 'Search requisitions by number, product name or code, requester, warehouse');
            $root.empty().append('<div class="vas-rqw-search"><label class="vas-rqw-sbox">' + U.icon.search +
                '<input type="search" autocomplete="off" placeholder="' + U.esc(ph) + '" aria-label="' + U.esc(ph) + '">' +
                '<button type="button" class="clr">' + U.esc(U.lbl('VAS_305_Clear', 'Clear')) + '</button></label></div>');
            $box = $root.find('.vas-rqw-sbox');
            $input = $box.find('input');
            $panel = $('<div class="vas-rqw-sres" role="listbox"></div>').appendTo('body');

            $input.on('input', onInput);
            $input.on('focus', function () { if (term.length >= MIN_LEN && items.length) { render(); } });
            $input.on('keydown', function (e) {
                if (e.key === 'Escape') { enterWhenReady = false; }
                if (e.key === 'Enter' && busy) {
                    // results not back yet: open the first match as soon as they arrive
                    e.preventDefault(); enterWhenReady = true; return;
                }
                if (!$panel.hasClass('on')) { return; }
                if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                    e.preventDefault();
                    if (!items.length) { return; }
                    active = (active + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
                    $panel.find('.it').removeClass('on').filter('[data-i="' + active + '"]').addClass('on')[0].scrollIntoView({ block: 'nearest' });
                } else if (e.key === 'Enter') {
                    e.preventDefault(); choose(active);
                } else if (e.key === 'Escape') {
                    hide();
                }
            });
            $box.find('.clr').on('click', function (e) {
                e.preventDefault();
                $input.val(''); onInput(); $input.trigger('focus');
            });
            $panel.on('mousedown', '.it', function (e) { e.preventDefault(); choose(Number($(this).attr('data-i'))); });
            self._docClick = function (e) {
                if (!$panel.hasClass('on')) { return; }
                if ($(e.target).closest($box).length || $(e.target).closest($panel).length) { return; }
                hide();
            };
            self._reflow = function () { if ($panel.hasClass('on')) { position(); } };
            $(document).on('mousedown', self._docClick);
            $(window).on('resize', self._reflow);
            document.addEventListener('scroll', self._reflow, true);
            self._stopWatch = U.whenRemoved($root[0], function () { self.disposeComponent(); });
        };

        this.reload = function () { };
        this.relayout = function () { if ($panel && $panel.hasClass('on')) { position(); } };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            if (self._stopWatch) { self._stopWatch(); self._stopWatch = null; }
            if (timer) { window.clearTimeout(timer); }
            req++; busy = false; enterWhenReady = false; // answers still on the way are ignored
            if (self._docClick) { $(document).off('mousedown', self._docClick); }
            if (self._reflow) { $(window).off('resize', self._reflow); document.removeEventListener('scroll', self._reflow, true); }
            if ($panel) { $panel.remove(); $panel = null; }
            self._docClick = null; self._reflow = null;
            $root.remove();
        };
    };

    U.makeWidget(VAS.VAS_305_RequisitionSearchWidget);
})(VAS, jQuery);
