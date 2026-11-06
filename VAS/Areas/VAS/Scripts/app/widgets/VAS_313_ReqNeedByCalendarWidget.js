/**
 * VAS_313_ReqNeedByCalendarWidget - 6x4 month calendar on the Requisition screen (and its Home copy).
 * Completed requisitions on their need-by dates (one entry per requisition and day), coloured
 * Late / Not ordered / Ordered (not received) / Received. Entries that do not fit a day cell go
 * behind "+N more" (opens the day list). Entry = requisition popup. Month pager + Today.
 * Backend: VAS_313_ReqNeedByCalendarWidget/GetMonth?year=&month=
 *
 * Labels / Message Keys
 *  VAS_313_NeedByCalendar  Need-by Calendar
 *  VAS_313_Subtitle        Requisitions by need-by date · click to open
 *  VAS_313_Late            late
 *  VAS_313_LateLegend      Late
 *  VAS_313_NotOrdered      Not ordered
 *  VAS_313_Ordered         Ordered
 *  VAS_313_Received        Received
 *  VAS_313_Today           Today
 *  VAS_313_More            more
 *  VAS_313_DueOn           Due on
 *  VAS_313_PrevMonth       Previous month
 *  VAS_313_NextMonth       Next month
 *  VAS_313_Requisitions    requisitions
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;

    VAS.VAS_313_ReqNeedByCalendarWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card, $cal, data = null, y = 0, m = 0, stopResize = null, req = 0;
        this.frame = null;
        this.windowNo = -1;

        function weekdayNames() {
            var base = new Date(2023, 0, 1); // a Sunday
            var out = [];
            for (var i = 0; i < 7; i++) {
                out.push(new Date(2023, 0, 1 + i).toLocaleDateString(window.navigator.language || 'en-US', { weekday: 'short' }));
            }
            return out;
        }

        function iso(d) { return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }

        this.Initalize = function () {
            var now = new Date();
            y = now.getFullYear(); m = now.getMonth() + 1;
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.cal, U.lbl('VAS_313_NeedByCalendar', 'Need-by Calendar'), U.lbl('VAS_313_Subtitle', 'Requisitions by need-by date · click to open'), 'late') +
                '<div class="vas-rqw-cal"></div>' +
                '<div class="vas-rqw-ft"><span class="vas-rqw-lg">' +
                '<span><i style="background:#D14545"></i>' + U.esc(U.lbl('VAS_313_LateLegend', 'Late')) + '</span>' +
                '<span><i style="background:#0083DA"></i>' + U.esc(U.lbl('VAS_313_NotOrdered', 'Not ordered')) + '</span>' +
                '<span><i style="background:#D78B10"></i>' + U.esc(U.lbl('VAS_313_Ordered', 'Ordered')) + '</span>' +
                '<span><i style="background:#12A383"></i>' + U.esc(U.lbl('VAS_313_Received', 'Received')) + '</span></span>' +
                '<span class="vas-rqw-pg"><button type="button" class="today" data-m="0">' + U.esc(U.lbl('VAS_313_Today', 'Today')) + '</button>' +
                '<button type="button" data-m="-1" aria-label="' + U.esc(U.lbl('VAS_313_PrevMonth', 'Previous month')) + '">' + U.icon.prev + '</button>' +
                '<span class="mon"></span>' +
                '<button type="button" data-m="1" aria-label="' + U.esc(U.lbl('VAS_313_NextMonth', 'Next month')) + '">' + U.icon.next + '</button></span></div>' +
                '<span class="vas-rqw-busy"><i></i></span></div>');
            $root.empty().append($card);
            $cal = $card.find('.vas-rqw-cal');
            $card.find('.vas-rqw-pg button').on('click', function () {
                var step = Number($(this).attr('data-m'));
                if (step === 0) { var n = new Date(); y = n.getFullYear(); m = n.getMonth() + 1; }
                else { m += step; if (m < 1) { m = 12; y--; } if (m > 12) { m = 1; y++; } }
                self.reload();
            });
            $cal.on('click', '.ev', function () { U.showRequisition(self, Number($(this).attr('data-r'))); });
            $cal.on('click', '.more', function () { openDay($(this).attr('data-d')); });
            stopResize = U.onResize($root[0], function () { self.relayout(); });
            self.reload();
        };

        this.reload = function () {
            if (!$card) { return; }
            var mine = ++req;
            $card.find('.mon').text(new Date(y, m - 1, 1).toLocaleDateString(window.navigator.language || 'en-US', { month: 'short', year: 'numeric' }));
            $card.find('.vas-rqw-busy').addClass('on');
            U.get('VAS_313_ReqNeedByCalendarWidget/GetMonth', { year: y, month: m }).done(function (d) {
                if (mine !== req) { return; }
                data = d;
                var $p = $card.find('[data-pill="late"]');
                $p.text(U.num(d.lateCount) + ' ' + U.lbl('VAS_313_Late', 'late')).toggleClass('d', d.lateCount > 0).toggleClass('s', !d.lateCount);
                self.relayout();
            }).fail(function (err) {
                if (mine !== req) { return; }
                data = null;
                $cal.html('<div class="vas-rqw-msg err" style="grid-column:1 / -1">' + U.esc(err) + '</div>');
            }).always(function () { if (mine === req) { $card.find('.vas-rqw-busy').removeClass('on'); } });
        };

        this.relayout = function () {
            if (!data || !$cal) { return; }
            var first = new Date(y, m - 1, 1);
            var dim = new Date(y, m, 0).getDate();
            var weeks = Math.ceil((first.getDay() + dim) / 7);
            var start = new Date(y, m - 1, 1 - first.getDay());
            var todayIso = data.today || U.todayIso();
            var byDay = {};
            (data.entries || []).forEach(function (e) { (byDay[e.date] = byDay[e.date] || []).push(e); });
            var html = weekdayNames().map(function (w) { return '<div class="wd">' + U.esc(w) + '</div>'; }).join('');
            for (var i = 0; i < weeks * 7; i++) {
                var d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
                var k = iso(d), out = d.getMonth() !== m - 1;
                var evs = out ? [] : (byDay[k] || []);
                html += '<div class="cd' + (out ? ' out' : '') + (k === todayIso ? ' td' : '') + (d.getDay() % 6 === 0 ? ' we' : '') + '" data-d="' + k + '">' +
                    '<span class="dn">' + d.getDate() + '</span><div class="evs">' +
                    evs.map(function (e) {
                        var tip = e.documentNo + ' · ' + (e.items || []).map(function (x) { return x.product; }).join(', ') + ' · ' + U.money(e.amount, data.currency);
                        return '<button type="button" class="ev s-' + e.state + '" data-r="' + e.requisitionId + '" title="' + U.esc(tip) + '"><b>' + U.esc(e.documentNo) + '</b>' +
                            U.esc(e.firstItem) + (e.lineCount > 1 ? ' +' + (e.lineCount - 1) : '') + '</button>';
                    }).join('') + '</div></div>';
            }
            $cal.html(html);
            // never scroll a day cell: hide what does not fit behind "+N more"
            $cal.find('.evs').each(function () {
                var box = this, items = $(box).children('.ev').toArray();
                if (box.scrollHeight <= box.clientHeight + 1 || !items.length) { return; }
                var $more = $('<button type="button" class="more"></button>').attr('data-d', $(box).parent().attr('data-d'));
                $(box).append($more);
                var n = items.length;
                do {
                    n--; items[n].style.display = 'none';
                    $more.text('+' + (items.length - n) + ' ' + U.lbl('VAS_313_More', 'more'));
                } while (n > 0 && box.scrollHeight > box.clientHeight + 1);
            });
        };

        function openDay(dayIso) {
            if (!data) { return; }
            var evs = (data.entries || []).filter(function (e) { return e.date === dayIso; });
            var cur = data.currency;
            var rows = evs.map(function (e) {
                return '<div class="vas-rqw-gr" data-r="' + e.requisitionId + '"><span class="b">' + U.esc(e.documentNo) + '</span>' +
                    '<span title="' + U.esc(e.firstItem) + '">' + U.esc(e.firstItem) + (e.lineCount > 1 ? ' +' + (e.lineCount - 1) : '') + '</span>' +
                    '<span class="m">' + U.esc(e.requester || '') + '</span><span class="vas-rqw-r">' + U.amount(e.amount, cur) + '</span>' +
                    '<span>' + U.stateChip(e.state === 'notordered' ? 'notordered' : e.state, e.date) + '</span></div>';
            });
            var title = U.lbl('VAS_313_DueOn', 'Due on') + ' ' + U.dateLong(dayIso);
            U.openModal({
                title: title,
                body: '<div class="vas-rqw-sh"><b>' + U.esc(title) + '</b><span>' + evs.length + ' ' + U.esc(U.unit(evs.length, 'VAS_313_Requisitions', 'requisitions')) + '</span></div>' +
                    U.table('minmax(0,1fr) minmax(0,1.8fr) minmax(0,1.2fr) 6em 7em', [3],
                        [U.lbl('VAS_305_Requisition', 'Requisition'), U.lbl('VAS_305_Product', 'Product'), U.lbl('VAS_305_Requester', 'Requested by'),
                        U.lbl('VAS_305_Value', 'Value'), U.lbl('VAS_305_Status', 'Status')], rows),
                after: function ($m) {
                    $m.find('.vas-rqw-pb').off('click.rqwd').on('click.rqwd', '.vas-rqw-gr[data-r]', function () {
                        U.showRequisition(self, Number($(this).attr('data-r')));
                    });
                }
            });
        }

        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if (stopResize) { stopResize(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_313_ReqNeedByCalendarWidget);
})(VAS, jQuery);
