(function () {
    const container = document.querySelector('#duplicatedEpisodeDetectPage:not(.hide)');
    const submitButton = container.querySelector('#start-scan-button');
    const deleteButton = container.querySelector('#remove-button');

    submitButton.addEventListener('click', async function (e) {
        e.preventDefault();
        try {
            Dashboard.showLoadingMsg();
            submitButton.setAttribute('disabled', 'disabled');

            container.querySelector('#detect-result').style.display = 'none';

            const request = await ApiClient.fetch({
                type: 'POST',
                url: '/Plugins/Bangumi/Tools/DuplicatedEpisodesDetector/Scan',
                data: {
                    length: container.querySelector('#check-length').checked,
                    specials: container.querySelector('#skip-specials').checked,
                },
            });
            const result = await request.json();

            Array.from(container.querySelector('.detect-result-list').children).forEach((item) => item.remove());

            if (result.length) {
                container.querySelector('.detect-result-empty').style.display = 'none';
                container.querySelector('.detect-result-list').style.display = '';
                container.querySelector('.remove-button-container').style.display = '';

                for (const item of result) {
                    const episodeElement = container.querySelector('#episode-template').content.cloneNode(true);
                    episodeElement.querySelector('h3').textContent = item.Title;
                    episodeElement.querySelector('a').href = 'https://bgm.tv/ep/' + item.BangumiId;

                    const itemsContainer = episodeElement.querySelector('.detect-result-files');

                    const latestModifiedTime = item.Items.map(x => x.LastModified).reduce((previous, latest) => previous.localeCompare(latest) > 0 ? previous : latest)

                    for (const file of item.Items) {
                        const itemElement = container.querySelector('#item-template').content.cloneNode(true);
                        itemElement.querySelector('input').value = file.Id;
                        itemElement.querySelector('input').checked = file.LastModified !== latestModifiedTime;
                        itemElement.querySelector('.item-file-path').textContent = file.Path;
                        itemElement.querySelector('.file-modified-time').textContent = formatDateTime(file.LastModified);
                        itemElement.querySelector('.duration').textContent = formatDuration(file.Ticks);
                        itemElement.querySelector('a').href = '#/details?id=' + file.Id;
                        itemsContainer.appendChild(itemElement);
                    }

                    container.querySelector('.detect-result-list').appendChild(episodeElement);
                }
            } else {
                container.querySelector('.detect-result-empty').style.display = '';
                container.querySelector('.detect-result-list').style.display = 'none';
                container.querySelector('.remove-button-container').style.display = 'none';
            }

            container.querySelector('#detect-result').style.display = '';
        } finally {
            Dashboard.hideLoadingMsg();
            submitButton.removeAttribute('disabled');
        }
    })

    deleteButton.addEventListener('click', async function (e) {
        e.preventDefault();
        try {
            const items = Array.from(container.querySelectorAll('.detect-result-files input[type="checkbox"]:checked')).map(x => x.value);

            if (items.length === 0) {
                Dashboard.alert('请先选择需要删除的文件')
                return
            }

            if (!await new Promise(resolve => Dashboard.confirm(`确定要删除已选择的 ${items.length} 个文件吗？此操作无法撤销，请仔细检查`, '删除重复剧集', resolve))) {
                return
            }

            Dashboard.showLoadingMsg();
            deleteButton.setAttribute('disabled', 'disabled');

            const request = await ApiClient.fetch({
                type: 'POST',
                url: '/Plugins/Bangumi/Tools/DuplicatedEpisodesDetector/Delete',
                data: { items },
            });

            if (!request.ok) {
                throw new Error(await request.text());
            }

            Dashboard.alert('所选文件已删除');

            // Refresh the scan results
            submitButton.click();
        } finally {
            Dashboard.hideLoadingMsg();
            deleteButton.removeAttribute('disabled');
        }
    })

    const dateTimeFormatter = new Intl.DateTimeFormat(undefined, {
        dateStyle: 'long',
        timeStyle: 'medium'
    });
    const durationFormatter = new Intl.DurationFormat();

    function formatDateTime(dateTimeStr) {
        const date = new Date(dateTimeStr);
        return dateTimeFormatter.format(date);
    }

    function formatDuration(ticks) {
        const seconds = Math.floor(ticks / 10000000) % 60;
        const minutes = Math.floor(ticks / 10000000 / 60) % 60;
        const hours = Math.floor(ticks / 10000000 / 60 / 60) % 24;

        return durationFormatter.format({
            hours,
            minutes,
            seconds
        });
    }
})();
