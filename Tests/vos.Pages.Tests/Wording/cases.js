(function () {
  var languages = ['en', 'de', 'es', 'fr', 'it', 'nl', 'ar'];
  var values = [0, 1, -1, 0.5, 0.045, 12.345, 999.95, 1234.5, -9876.543, 1234567.891, 1e9, 2.5e12, 0.000123, 1 / 3, 1.005, 2.5, 3.5, -0.4];
  var formats = {
    plain: {},
    integer: {},
    decimal1: { minimumFractionDigits: 1, maximumFractionDigits: 1 },
    decimal2: { minimumFractionDigits: 2, maximumFractionDigits: 2 },
    percent: { style: 'percent', maximumFractionDigits: 0 },
    percent1: { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 },
    money: { style: 'currency', currency: 'USD' },
    compact: { notation: 'compact', maximumFractionDigits: 1 },
    bytes: { maximumFractionDigits: 1 }
  };
  var names = ['Zürich', 'zebra', 'Zebra', 'Århus', 'Aarhus', 'Étang', 'etang', 'Eta', 'Ölberg', 'Olberg', 'Östra', 'ñandú', 'nandu', 'Núñez', 'Straße', 'Strasse', 'İstanbul', 'Istanbul', 'array 10', 'array 2', 'Array-1', 'array_1', 'ابار', 'بئر', 'Łódź', 'Lodz', 'čaj', 'caj', 'd', 'D', 'é', 'e', 'E', '', ' a', 'a b', 'ab', 'a-b', 'IJssel', 'ijs', '日本', '中文'];
  var worded = {};
  function word(key, make) {
    try { worded[key] = make(); } catch (refused) { worded[key] = 'THROWS ' + String(refused); }
  }
  languages.forEach(function (language) {
    Object.keys(formats).forEach(function (format) {
      values.forEach(function (value) {
        word('number|' + language + '|' + format + '|' + value, function () {
          var options = Object.assign({ numberingSystem: 'latn' }, formats[format]);
          return new Intl.NumberFormat(language, options).format(format === 'integer' ? Math.round(value) : value);
        });
      });
    });
    word('months|' + language, function () {
      var month = new Intl.DateTimeFormat(language, { month: 'short', timeZone: 'UTC' });
      var list = [];
      for (var m = 0; m < 12; m++) list.push(month.format(new Date(Date.UTC(2001, m, 1))));
      return list.join('|');
    });
    [1, 60, 366].forEach(function (day) {
      word('day|' + language + '|' + day, function () {
        return new Intl.DateTimeFormat(language, { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2000, 0, day)));
      });
    });
    [[-3, 'hour'], [1, 'day'], [-1, 'day'], [0, 'second'], [2, 'week'], [-5, 'month'], [-1, 'year']].forEach(function (ago) {
      word('relative|' + language + '|' + ago[0] + ago[1], function () {
        return new Intl.RelativeTimeFormat(language, { numeric: 'auto', numberingSystem: 'latn' }).format(ago[0], ago[1]);
      });
    });
    word('name order|' + language, function () {
      return names.slice().sort(function (a, b) { return a.localeCompare(b, language); }).join('|');
    });
  });
  names.forEach(function (name) {
    word('page address|' + name, function () {
      return name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
    });
  });
  return JSON.stringify(worded, null, 1);
})()
