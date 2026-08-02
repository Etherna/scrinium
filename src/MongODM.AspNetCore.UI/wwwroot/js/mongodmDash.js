(function () {
    'use strict';

    var POLL_IDLE_MS = 3000;
    var POLL_ACTIVE_MS = 1000;
    var FEEDBACK_TIMEOUT_MS = 5000;

    var baseUrl = window.location.pathname;
    var banner = document.getElementById('connection-banner');
    var allCards = Array.prototype.slice.call(document.querySelectorAll('.dbcontext-card'));
    //read-only db contexts render as static cards, with no migration controls to drive
    var cards = allCards.filter(function (card) {
        return card.dataset.readOnly !== 'true';
    });
    var pollTimer = null;

    //model schemas are available on every db context, read-only ones included
    allCards.forEach(function (card) {
        var section = card.querySelector('[data-role="schemas"]');
        section.addEventListener('toggle', function () {
            /* Size the collections lazily: it reads their metadata, a constant cost.
             * The schema ids count scans a whole collection, and stays on demand. */
            if (section.open && !section.dataset.loaded)
                loadCollectionSizes(card);
        });

        Array.prototype.forEach.call(card.querySelectorAll('.schema-collection'), function (collection) {
            collection.querySelector('[data-role="count-schemas"]').addEventListener('click', function () {
                loadSchemaCounts(card, collection);
            });
        });
    });

    cards.forEach(function (card) {
        card.querySelector('[data-role="start"]').addEventListener('click', function () {
            startMigration(card, false);
        });
        card.querySelector('[data-role="start-dry-run"]').addEventListener('click', function () {
            startMigration(card, true);
        });
    });

    if (cards.length !== 0)
        refreshStatus();

    function startMigration(card, dryRun) {
        var identifier = card.dataset.identifier;
        var stopAtFirstError = card.querySelector('[data-role="stop-at-first-error"]').checked;
        var message = dryRun
            ? 'Start migration dry run on "' + identifier + '"?\n\n' +
              'The dry run simulates the migration without persisting anything, reporting the ' +
              'failing documents. Data stays accessible while it runs.'
            : 'Start migration on "' + identifier + '"?\n\n' +
              'While the migration is running, the db context denies concurrent access to data.';
        message += stopAtFirstError
            ? '\n\nIt stops at the first failing document.'
            : '\n\nFailing documents are skipped and reported, without stopping the scan.';
        if (!window.confirm(message))
            return;

        card.querySelector('[data-role="start"]').disabled = true;
        card.querySelector('[data-role="start-dry-run"]').disabled = true;

        fetch(baseUrl + '?handler=StartMigration', {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams({
                identifier: identifier,
                dryRun: dryRun,
                stopAtFirstError: stopAtFirstError
            })
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

    function loadCollectionSizes(card) {
        var section = card.querySelector('[data-role="schemas"]');
        section.dataset.loaded = 'true';

        fetch(baseUrl + '?handler=CollectionSizes&identifier=' + encodeURIComponent(card.dataset.identifier), {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (collections) {
            collections.forEach(function (collection) {
                var element = findCollection(card, collection.repository);
                if (!element)
                    return;

                element.querySelector('[data-role="collection-size"]').textContent = collection.isUnavailable
                    ? 'size unavailable: an exclusive access is running'
                    : 'about ' + collection.estimatedDocumentsCount.toLocaleString() +
                      (collection.estimatedDocumentsCount === 1 ? ' document' : ' documents');
                element.querySelector('[data-role="count-schemas"]').disabled = collection.isUnavailable;
            });
        }).catch(function () {
            section.dataset.loaded = '';
            Array.prototype.forEach.call(card.querySelectorAll('[data-role="collection-size"]'), function (size) {
                size.textContent = 'size request failed';
            });
        });
    }

    function loadSchemaCounts(card, collection) {
        var button = collection.querySelector('[data-role="count-schemas"]');
        button.disabled = true;
        button.textContent = 'Counting…';

        fetch(baseUrl + '?handler=SchemaCounts' +
            '&identifier=' + encodeURIComponent(card.dataset.identifier) +
            '&repositoryName=' + encodeURIComponent(collection.dataset.repository), {
            headers: { 'Accept': 'application/json' }
        }).then(function (response) {
            if (!response.ok)
                throw new Error('HTTP ' + response.status);
            return response.json();
        }).then(function (counts) {
            renderSchemaCounts(collection, counts);
            button.textContent = counts.isUnavailable ? 'Count documents' : 'Recount';
        }).catch(function () {
            button.textContent = 'Count failed, retry';
        }).then(function () {
            button.disabled = false;
        });
    }

    function findCollection(card, repository) {
        var found = null;
        Array.prototype.forEach.call(card.querySelectorAll('.schema-collection'), function (candidate) {
            if (candidate.dataset.repository === repository)
                found = candidate;
        });
        return found;
    }

    function renderSchemaCounts(element, collection) {
        var body = element.querySelector('tbody');

        // Drop the rows added by a previous count, and reset the registered schema counts.
        Array.prototype.forEach.call(body.querySelectorAll('[data-role="extra-row"]'), function (row) {
            body.removeChild(row);
        });

        var schemaRows = body.querySelectorAll('[data-schema-id]');
        Array.prototype.forEach.call(schemaRows, function (row) {
            setCount(row.querySelector('[data-role="count"]'), collection.isUnavailable ? null : 0, false);
        });

        if (collection.isUnavailable)
            return;

        // Fill the counts of the registered schemas, adding a row for each unrecognized one.
        collection.schemaCounts.forEach(function (schemaCount) {
            var row = null;
            Array.prototype.forEach.call(schemaRows, function (candidate) {
                if (candidate.dataset.schemaId === schemaCount.schemaId)
                    row = candidate;
            });

            if (row)
                setCount(row.querySelector('[data-role="count"]'),
                    schemaCount.documentsCount,
                    row.dataset.active !== 'true');
            else
                body.appendChild(buildExtraRow(schemaCount.schemaId, 'unrecognized', schemaCount.documentsCount));
        });

        if (collection.documentsWithoutSchemaId > 0)
            body.appendChild(buildExtraRow(null, 'missing', collection.documentsWithoutSchemaId));
    }

    function buildExtraRow(schemaId, kind, documentsCount) {
        var row = document.createElement('tr');
        row.dataset.role = 'extra-row';

        var modelTypeCell = document.createElement('td');
        modelTypeCell.className = 'muted';
        modelTypeCell.textContent = '—';
        row.appendChild(modelTypeCell);

        var schemaCell = document.createElement('td');
        if (schemaId !== null) {
            var schemaIdLabel = document.createElement('span');
            schemaIdLabel.className = 'schema-id';
            schemaIdLabel.textContent = schemaId;
            schemaCell.appendChild(schemaIdLabel);
            schemaCell.appendChild(document.createTextNode(' '));
        }
        var tag = document.createElement('span');
        tag.className = 'schema-tag ' + kind;
        tag.textContent = kind === 'missing' ? 'no schema id' : kind;
        schemaCell.appendChild(tag);
        row.appendChild(schemaCell);

        var countCell = document.createElement('td');
        countCell.dataset.role = 'count';
        setCount(countCell, documentsCount, true);
        row.appendChild(countCell);

        return row;
    }

    function setCount(cell, documentsCount, needsMigration) {
        if (documentsCount === null) {
            cell.textContent = '—';
            cell.className = 'numeric muted';
            return;
        }

        cell.textContent = documentsCount.toLocaleString();
        cell.className = documentsCount === 0
            ? 'numeric muted'
            : 'numeric' + (needsMigration ? ' needs-migration' : '');
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
            badge.textContent = status.runningOperation.isDryRun ? 'Dry run' : 'Migrating';
            badge.className = 'status-badge running';
        } else if (status.isLocked) {
            badge.textContent = 'Locked';
            badge.className = 'status-badge locked';
        } else {
            badge.textContent = 'Idle';
            badge.className = 'status-badge idle';
        }

        card.querySelector('[data-role="start"]').disabled = status.isLocked;
        card.querySelector('[data-role="start-dry-run"]').disabled = status.isLocked;
        card.querySelector('[data-role="stop-at-first-error"]').disabled = status.isLocked;

        var live = card.querySelector('[data-role="live"]');
        var logList = card.querySelector('[data-role="logs"]');
        if (status.runningOperation) {
            live.hidden = false;
            card.querySelector('[data-role="live-meta"]').textContent =
                (status.runningOperation.isDryRun ? 'Dry run operation ' : 'Operation ') +
                status.runningOperation.id +
                ' — ' + status.runningOperation.status +
                ' since ' + formatDateTime(status.runningOperation.creationDateTime) +
                (status.runningOperation.stopAtFirstError ? ' — stops at the first failing document' : '');
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

            //failing documents reported by the migration
            if (log.errors && log.errors.length) {
                var errorsEntry = document.createElement('li');
                errorsEntry.className = 'log-errors';
                var errorsList = document.createElement('ul');
                log.errors.forEach(function (error) {
                    var errorItem = document.createElement('li');
                    errorItem.textContent = error.documentId + ' — ' + error.message;
                    errorsList.appendChild(errorItem);
                });
                errorsEntry.appendChild(errorsList);
                list.appendChild(errorsEntry);
            }
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

            if (operation.isDryRun) {
                var dryRunBadge = document.createElement('span');
                dryRunBadge.className = 'status-badge dry-run';
                dryRunBadge.textContent = 'Dry run';
                summary.appendChild(dryRunBadge);
            }

            if (operation.stopAtFirstError) {
                var stopBadge = document.createElement('span');
                stopBadge.className = 'status-badge';
                stopBadge.textContent = 'Stop at first error';
                summary.appendChild(stopBadge);
            }

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
