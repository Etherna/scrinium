(function () {
    'use strict';

    var POLL_IDLE_MS = 3000;
    var POLL_ACTIVE_MS = 1000;
    var FEEDBACK_TIMEOUT_MS = 5000;

    var baseUrl = window.location.pathname;
    var banner = document.getElementById('connection-banner');
    var cards = Array.prototype.slice.call(document.querySelectorAll('.dbcontext-card'));
    var pollTimer = null;

    if (cards.length === 0)
        return;

    cards.forEach(function (card) {
        card.querySelector('[data-role="start"]').addEventListener('click', function () {
            startMigration(card);
        });
    });

    refreshStatus();

    function startMigration(card) {
        var identifier = card.dataset.identifier;
        var message = 'Start migration on "' + identifier + '"?\n\n' +
            'While the migration is running, the db context denies concurrent access to data.';
        if (!window.confirm(message))
            return;

        var startBtn = card.querySelector('[data-role="start"]');
        startBtn.disabled = true;

        fetch(baseUrl + '?handler=StartMigration', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams({ identifier: identifier })
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (result) {
            if (!result.started)
                showFeedback(card, 'Migration not started: another operation is already in progress.');
            refreshStatus();
        }).catch(function () {
            showFeedback(card, 'Migration start request failed.');
            refreshStatus();
        });
    }

    function refreshStatus() {
        fetch(baseUrl + '?handler=Status', {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (statuses) {
            banner.hidden = true;

            var anyLocked = false;
            statuses.forEach(function (status) {
                if (status.isLocked)
                    anyLocked = true;

                cards.forEach(function (card) {
                    if (card.dataset.identifier === status.identifier)
                        renderCard(card, status);
                });
            });

            schedule(anyLocked ? POLL_ACTIVE_MS : POLL_IDLE_MS);
        }).catch(function () {
            banner.hidden = false;
            schedule(POLL_IDLE_MS);
        });
    }

    function schedule(delayMs) {
        window.clearTimeout(pollTimer);
        pollTimer = window.setTimeout(refreshStatus, delayMs);
    }

    function renderCard(card, status) {
        // Skip DOM rebuild when nothing changed, it would close open <details> and reset scroll.
        var payload = JSON.stringify(status);
        if (card.dataset.lastPayload === payload)
            return;
        card.dataset.lastPayload = payload;

        var badge = card.querySelector('[data-role="status"]');
        if (status.runningOperation) {
            badge.textContent = 'Migrating';
            badge.className = 'status-badge running';
        } else if (status.isLocked) {
            badge.textContent = 'Locked';
            badge.className = 'status-badge locked';
        } else {
            badge.textContent = 'Idle';
            badge.className = 'status-badge idle';
        }

        card.querySelector('[data-role="start"]').disabled = status.isLocked;

        var live = card.querySelector('[data-role="live"]');
        var logList = card.querySelector('[data-role="logs"]');
        if (status.runningOperation) {
            live.hidden = false;
            card.querySelector('[data-role="live-meta"]').textContent =
                'Operation ' + status.runningOperation.id +
                ' — ' + status.runningOperation.status +
                ' since ' + formatDateTime(status.runningOperation.creationDateTime);
            renderLogs(logList, status.runningOperation.logs, true);
        } else {
            live.hidden = true;
            logList.innerHTML = '';
        }

        renderHistory(card.querySelector('[data-role="history"]'), status.lastOperations);
    }

    function renderLogs(list, logs, scrollToBottom) {
        list.innerHTML = '';
        logs.forEach(function (log) {
            var entry = document.createElement('li');
            entry.className = 'log-entry ' + log.state.toLowerCase();

            var time = document.createElement('time');
            time.textContent = formatTime(log.creationDateTime);
            entry.appendChild(time);

            var state = document.createElement('span');
            state.className = 'log-state';
            state.textContent = log.state.toUpperCase();
            entry.appendChild(state);

            var description = document.createElement('span');
            description.textContent = log.description;
            entry.appendChild(description);

            list.appendChild(entry);
        });

        if (scrollToBottom)
            list.scrollTop = list.scrollHeight;
    }

    function renderHistory(container, operations) {
        container.innerHTML = '';

        if (operations.length === 0) {
            var empty = document.createElement('p');
            empty.className = 'muted';
            empty.textContent = 'No migrations executed yet.';
            container.appendChild(empty);
            return;
        }

        operations.forEach(function (operation) {
            var entry = document.createElement('details');
            entry.className = 'history-entry';

            var summary = document.createElement('summary');

            var badge = document.createElement('span');
            badge.className = 'status-badge ' + historyBadgeClass(operation.status);
            badge.textContent = operation.status;
            summary.appendChild(badge);

            var dates = document.createElement('span');
            dates.className = 'history-dates';
            dates.textContent = 'started ' + formatDateTime(operation.creationDateTime) +
                (operation.completedDateTime ? ' — completed ' + formatDateTime(operation.completedDateTime) : '');
            summary.appendChild(dates);

            entry.appendChild(summary);

            var logList = document.createElement('ol');
            logList.className = 'log-list';
            renderLogs(logList, operation.logs, false);
            entry.appendChild(logList);

            container.appendChild(entry);
        });
    }

    function historyBadgeClass(status) {
        switch (status) {
            case 'Completed': return 'idle';
            case 'Failed': return 'locked';
            case 'Running':
            case 'New': return 'running';
            default: return '';
        }
    }

    function formatDateTime(value) {
        return value ? new Date(value).toLocaleString() : 'unknown time';
    }

    function formatTime(value) {
        return value ? new Date(value).toLocaleTimeString() : '';
    }
})();
