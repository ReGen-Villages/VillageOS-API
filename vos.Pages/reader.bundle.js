"use strict";
(() => {
  // vos.Tendril/node_modules/i18next/dist/esm/i18next.js
  var isString = (obj) => typeof obj === "string";
  var defer = () => {
    let res;
    let rej;
    const promise = new Promise((resolve, reject) => {
      res = resolve;
      rej = reject;
    });
    promise.resolve = res;
    promise.reject = rej;
    return promise;
  };
  var makeString = (object) => {
    if (object == null) return "";
    return "" + object;
  };
  var copy = (a, s, t2) => {
    a.forEach((m) => {
      if (s[m]) t2[m] = s[m];
    });
  };
  var lastOfPathSeparatorRegExp = /###/g;
  var cleanKey = (key) => key && key.indexOf("###") > -1 ? key.replace(lastOfPathSeparatorRegExp, ".") : key;
  var canNotTraverseDeeper = (object) => !object || isString(object);
  var getLastOfPath = (object, path, Empty) => {
    const stack = !isString(path) ? path : path.split(".");
    let stackIndex = 0;
    while (stackIndex < stack.length - 1) {
      if (canNotTraverseDeeper(object)) return {};
      const key = cleanKey(stack[stackIndex]);
      if (!object[key] && Empty) object[key] = new Empty();
      if (Object.prototype.hasOwnProperty.call(object, key)) {
        object = object[key];
      } else {
        object = {};
      }
      ++stackIndex;
    }
    if (canNotTraverseDeeper(object)) return {};
    return {
      obj: object,
      k: cleanKey(stack[stackIndex])
    };
  };
  var setPath = (object, path, newValue) => {
    const {
      obj,
      k
    } = getLastOfPath(object, path, Object);
    if (obj !== void 0 || path.length === 1) {
      obj[k] = newValue;
      return;
    }
    let e = path[path.length - 1];
    let p = path.slice(0, path.length - 1);
    let last = getLastOfPath(object, p, Object);
    while (last.obj === void 0 && p.length) {
      e = `${p[p.length - 1]}.${e}`;
      p = p.slice(0, p.length - 1);
      last = getLastOfPath(object, p, Object);
      if (last?.obj && typeof last.obj[`${last.k}.${e}`] !== "undefined") {
        last.obj = void 0;
      }
    }
    last.obj[`${last.k}.${e}`] = newValue;
  };
  var pushPath = (object, path, newValue, concat) => {
    const {
      obj,
      k
    } = getLastOfPath(object, path, Object);
    obj[k] = obj[k] || [];
    obj[k].push(newValue);
  };
  var getPath = (object, path) => {
    const {
      obj,
      k
    } = getLastOfPath(object, path);
    if (!obj) return void 0;
    if (!Object.prototype.hasOwnProperty.call(obj, k)) return void 0;
    return obj[k];
  };
  var getPathWithDefaults = (data, defaultData, key) => {
    const value = getPath(data, key);
    if (value !== void 0) {
      return value;
    }
    return getPath(defaultData, key);
  };
  var deepExtend = (target, source, overwrite) => {
    for (const prop in source) {
      if (prop !== "__proto__" && prop !== "constructor") {
        if (prop in target) {
          if (isString(target[prop]) || target[prop] instanceof String || isString(source[prop]) || source[prop] instanceof String) {
            if (overwrite) target[prop] = source[prop];
          } else {
            deepExtend(target[prop], source[prop], overwrite);
          }
        } else {
          target[prop] = source[prop];
        }
      }
    }
    return target;
  };
  var regexEscape = (str) => str.replace(/[\-\[\]\/\{\}\(\)\*\+\?\.\\\^\$\|]/g, "\\$&");
  var _entityMap = {
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
    "/": "&#x2F;"
  };
  var escape = (data) => {
    if (isString(data)) {
      return data.replace(/[&<>"'\/]/g, (s) => _entityMap[s]);
    }
    return data;
  };
  var RegExpCache = class {
    constructor(capacity) {
      this.capacity = capacity;
      this.regExpMap = /* @__PURE__ */ new Map();
      this.regExpQueue = [];
    }
    getRegExp(pattern) {
      const regExpFromCache = this.regExpMap.get(pattern);
      if (regExpFromCache !== void 0) {
        return regExpFromCache;
      }
      const regExpNew = new RegExp(pattern);
      if (this.regExpQueue.length === this.capacity) {
        this.regExpMap.delete(this.regExpQueue.shift());
      }
      this.regExpMap.set(pattern, regExpNew);
      this.regExpQueue.push(pattern);
      return regExpNew;
    }
  };
  var chars = [" ", ",", "?", "!", ";"];
  var looksLikeObjectPathRegExpCache = new RegExpCache(20);
  var looksLikeObjectPath = (key, nsSeparator, keySeparator) => {
    nsSeparator = nsSeparator || "";
    keySeparator = keySeparator || "";
    const possibleChars = chars.filter((c) => nsSeparator.indexOf(c) < 0 && keySeparator.indexOf(c) < 0);
    if (possibleChars.length === 0) return true;
    const r = looksLikeObjectPathRegExpCache.getRegExp(`(${possibleChars.map((c) => c === "?" ? "\\?" : c).join("|")})`);
    let matched = !r.test(key);
    if (!matched) {
      const ki = key.indexOf(keySeparator);
      if (ki > 0 && !r.test(key.substring(0, ki))) {
        matched = true;
      }
    }
    return matched;
  };
  var deepFind = (obj, path, keySeparator = ".") => {
    if (!obj) return void 0;
    if (obj[path]) {
      if (!Object.prototype.hasOwnProperty.call(obj, path)) return void 0;
      return obj[path];
    }
    const tokens = path.split(keySeparator);
    let current = obj;
    for (let i = 0; i < tokens.length; ) {
      if (!current || typeof current !== "object") {
        return void 0;
      }
      let next;
      let nextPath = "";
      for (let j = i; j < tokens.length; ++j) {
        if (j !== i) {
          nextPath += keySeparator;
        }
        nextPath += tokens[j];
        next = current[nextPath];
        if (next !== void 0) {
          if (["string", "number", "boolean"].indexOf(typeof next) > -1 && j < tokens.length - 1) {
            continue;
          }
          i += j - i + 1;
          break;
        }
      }
      current = next;
    }
    return current;
  };
  var getCleanedCode = (code) => code?.replace(/_/g, "-");
  var consoleLogger = {
    type: "logger",
    log(args) {
      this.output("log", args);
    },
    warn(args) {
      this.output("warn", args);
    },
    error(args) {
      this.output("error", args);
    },
    output(type, args) {
      console?.[type]?.apply?.(console, args);
    }
  };
  var Logger = class _Logger {
    constructor(concreteLogger, options = {}) {
      this.init(concreteLogger, options);
    }
    init(concreteLogger, options = {}) {
      this.prefix = options.prefix || "i18next:";
      this.logger = concreteLogger || consoleLogger;
      this.options = options;
      this.debug = options.debug;
    }
    log(...args) {
      return this.forward(args, "log", "", true);
    }
    warn(...args) {
      return this.forward(args, "warn", "", true);
    }
    error(...args) {
      return this.forward(args, "error", "");
    }
    deprecate(...args) {
      return this.forward(args, "warn", "WARNING DEPRECATED: ", true);
    }
    forward(args, lvl, prefix, debugOnly) {
      if (debugOnly && !this.debug) return null;
      if (isString(args[0])) args[0] = `${prefix}${this.prefix} ${args[0]}`;
      return this.logger[lvl](args);
    }
    create(moduleName) {
      return new _Logger(this.logger, {
        ...{
          prefix: `${this.prefix}:${moduleName}:`
        },
        ...this.options
      });
    }
    clone(options) {
      options = options || this.options;
      options.prefix = options.prefix || this.prefix;
      return new _Logger(this.logger, options);
    }
  };
  var baseLogger = new Logger();
  var EventEmitter = class {
    constructor() {
      this.observers = {};
    }
    on(events, listener) {
      events.split(" ").forEach((event) => {
        if (!this.observers[event]) this.observers[event] = /* @__PURE__ */ new Map();
        const numListeners = this.observers[event].get(listener) || 0;
        this.observers[event].set(listener, numListeners + 1);
      });
      return this;
    }
    off(event, listener) {
      if (!this.observers[event]) return;
      if (!listener) {
        delete this.observers[event];
        return;
      }
      this.observers[event].delete(listener);
    }
    emit(event, ...args) {
      if (this.observers[event]) {
        const cloned = Array.from(this.observers[event].entries());
        cloned.forEach(([observer, numTimesAdded]) => {
          for (let i = 0; i < numTimesAdded; i++) {
            observer(...args);
          }
        });
      }
      if (this.observers["*"]) {
        const cloned = Array.from(this.observers["*"].entries());
        cloned.forEach(([observer, numTimesAdded]) => {
          for (let i = 0; i < numTimesAdded; i++) {
            observer.apply(observer, [event, ...args]);
          }
        });
      }
    }
  };
  var ResourceStore = class extends EventEmitter {
    constructor(data, options = {
      ns: ["translation"],
      defaultNS: "translation"
    }) {
      super();
      this.data = data || {};
      this.options = options;
      if (this.options.keySeparator === void 0) {
        this.options.keySeparator = ".";
      }
      if (this.options.ignoreJSONStructure === void 0) {
        this.options.ignoreJSONStructure = true;
      }
    }
    addNamespaces(ns) {
      if (this.options.ns.indexOf(ns) < 0) {
        this.options.ns.push(ns);
      }
    }
    removeNamespaces(ns) {
      const index = this.options.ns.indexOf(ns);
      if (index > -1) {
        this.options.ns.splice(index, 1);
      }
    }
    getResource(lng, ns, key, options = {}) {
      const keySeparator = options.keySeparator !== void 0 ? options.keySeparator : this.options.keySeparator;
      const ignoreJSONStructure = options.ignoreJSONStructure !== void 0 ? options.ignoreJSONStructure : this.options.ignoreJSONStructure;
      let path;
      if (lng.indexOf(".") > -1) {
        path = lng.split(".");
      } else {
        path = [lng, ns];
        if (key) {
          if (Array.isArray(key)) {
            path.push(...key);
          } else if (isString(key) && keySeparator) {
            path.push(...key.split(keySeparator));
          } else {
            path.push(key);
          }
        }
      }
      const result = getPath(this.data, path);
      if (!result && !ns && !key && lng.indexOf(".") > -1) {
        lng = path[0];
        ns = path[1];
        key = path.slice(2).join(".");
      }
      if (result || !ignoreJSONStructure || !isString(key)) return result;
      return deepFind(this.data?.[lng]?.[ns], key, keySeparator);
    }
    addResource(lng, ns, key, value, options = {
      silent: false
    }) {
      const keySeparator = options.keySeparator !== void 0 ? options.keySeparator : this.options.keySeparator;
      let path = [lng, ns];
      if (key) path = path.concat(keySeparator ? key.split(keySeparator) : key);
      if (lng.indexOf(".") > -1) {
        path = lng.split(".");
        value = ns;
        ns = path[1];
      }
      this.addNamespaces(ns);
      setPath(this.data, path, value);
      if (!options.silent) this.emit("added", lng, ns, key, value);
    }
    addResources(lng, ns, resources, options = {
      silent: false
    }) {
      for (const m in resources) {
        if (isString(resources[m]) || Array.isArray(resources[m])) this.addResource(lng, ns, m, resources[m], {
          silent: true
        });
      }
      if (!options.silent) this.emit("added", lng, ns, resources);
    }
    addResourceBundle(lng, ns, resources, deep, overwrite, options = {
      silent: false,
      skipCopy: false
    }) {
      let path = [lng, ns];
      if (lng.indexOf(".") > -1) {
        path = lng.split(".");
        deep = resources;
        resources = ns;
        ns = path[1];
      }
      this.addNamespaces(ns);
      let pack = getPath(this.data, path) || {};
      if (!options.skipCopy) resources = JSON.parse(JSON.stringify(resources));
      if (deep) {
        deepExtend(pack, resources, overwrite);
      } else {
        pack = {
          ...pack,
          ...resources
        };
      }
      setPath(this.data, path, pack);
      if (!options.silent) this.emit("added", lng, ns, resources);
    }
    removeResourceBundle(lng, ns) {
      if (this.hasResourceBundle(lng, ns)) {
        delete this.data[lng][ns];
      }
      this.removeNamespaces(ns);
      this.emit("removed", lng, ns);
    }
    hasResourceBundle(lng, ns) {
      return this.getResource(lng, ns) !== void 0;
    }
    getResourceBundle(lng, ns) {
      if (!ns) ns = this.options.defaultNS;
      return this.getResource(lng, ns);
    }
    getDataByLanguage(lng) {
      return this.data[lng];
    }
    hasLanguageSomeTranslations(lng) {
      const data = this.getDataByLanguage(lng);
      const n = data && Object.keys(data) || [];
      return !!n.find((v) => data[v] && Object.keys(data[v]).length > 0);
    }
    toJSON() {
      return this.data;
    }
  };
  var postProcessor = {
    processors: {},
    addPostProcessor(module) {
      this.processors[module.name] = module;
    },
    handle(processors, value, key, options, translator) {
      processors.forEach((processor) => {
        value = this.processors[processor]?.process(value, key, options, translator) ?? value;
      });
      return value;
    }
  };
  var PATH_KEY = /* @__PURE__ */ Symbol("i18next/PATH_KEY");
  function createProxy() {
    const state = [];
    const handler = /* @__PURE__ */ Object.create(null);
    let proxy;
    handler.get = (target, key) => {
      proxy?.revoke?.();
      if (key === PATH_KEY) return state;
      state.push(key);
      proxy = Proxy.revocable(target, handler);
      return proxy.proxy;
    };
    return Proxy.revocable(/* @__PURE__ */ Object.create(null), handler).proxy;
  }
  function keysFromSelector(selector, opts) {
    const {
      [PATH_KEY]: path
    } = selector(createProxy());
    const keySeparator = opts?.keySeparator ?? ".";
    const nsSeparator = opts?.nsSeparator ?? ":";
    if (path.length > 1 && nsSeparator) {
      const ns = opts?.ns;
      const nsArray = Array.isArray(ns) ? ns : null;
      if (nsArray && nsArray.length > 1 && nsArray.slice(1).includes(path[0])) {
        return `${path[0]}${nsSeparator}${path.slice(1).join(keySeparator)}`;
      }
    }
    return path.join(keySeparator);
  }
  var checkedLoadedFor = {};
  var shouldHandleAsObject = (res) => !isString(res) && typeof res !== "boolean" && typeof res !== "number";
  var Translator = class _Translator extends EventEmitter {
    constructor(services, options = {}) {
      super();
      copy(["resourceStore", "languageUtils", "pluralResolver", "interpolator", "backendConnector", "i18nFormat", "utils"], services, this);
      this.options = options;
      if (this.options.keySeparator === void 0) {
        this.options.keySeparator = ".";
      }
      this.logger = baseLogger.create("translator");
    }
    changeLanguage(lng) {
      if (lng) this.language = lng;
    }
    exists(key, o = {
      interpolation: {}
    }) {
      const opt = {
        ...o
      };
      if (key == null) return false;
      const resolved = this.resolve(key, opt);
      if (resolved?.res === void 0) return false;
      const isObject = shouldHandleAsObject(resolved.res);
      if (opt.returnObjects === false && isObject) {
        return false;
      }
      return true;
    }
    extractFromKey(key, opt) {
      let nsSeparator = opt.nsSeparator !== void 0 ? opt.nsSeparator : this.options.nsSeparator;
      if (nsSeparator === void 0) nsSeparator = ":";
      const keySeparator = opt.keySeparator !== void 0 ? opt.keySeparator : this.options.keySeparator;
      let namespaces = opt.ns || this.options.defaultNS || [];
      const wouldCheckForNsInKey = nsSeparator && key.indexOf(nsSeparator) > -1;
      const seemsNaturalLanguage = !this.options.userDefinedKeySeparator && !opt.keySeparator && !this.options.userDefinedNsSeparator && !opt.nsSeparator && !looksLikeObjectPath(key, nsSeparator, keySeparator);
      if (wouldCheckForNsInKey && !seemsNaturalLanguage) {
        const m = key.match(this.interpolator.nestingRegexp);
        if (m && m.length > 0) {
          return {
            key,
            namespaces: isString(namespaces) ? [namespaces] : namespaces
          };
        }
        const parts = key.split(nsSeparator);
        if (nsSeparator !== keySeparator || nsSeparator === keySeparator && this.options.ns.indexOf(parts[0]) > -1) namespaces = parts.shift();
        key = parts.join(keySeparator);
      }
      return {
        key,
        namespaces: isString(namespaces) ? [namespaces] : namespaces
      };
    }
    translate(keys, o, lastKey) {
      let opt = typeof o === "object" ? {
        ...o
      } : o;
      if (typeof opt !== "object" && this.options.overloadTranslationOptionHandler) {
        opt = this.options.overloadTranslationOptionHandler(arguments);
      }
      if (typeof opt === "object") opt = {
        ...opt
      };
      if (!opt) opt = {};
      if (keys == null) return "";
      if (typeof keys === "function") keys = keysFromSelector(keys, {
        ...this.options,
        ...opt
      });
      if (!Array.isArray(keys)) keys = [String(keys)];
      keys = keys.map((k) => typeof k === "function" ? keysFromSelector(k, {
        ...this.options,
        ...opt
      }) : String(k));
      const returnDetails = opt.returnDetails !== void 0 ? opt.returnDetails : this.options.returnDetails;
      const keySeparator = opt.keySeparator !== void 0 ? opt.keySeparator : this.options.keySeparator;
      const {
        key,
        namespaces
      } = this.extractFromKey(keys[keys.length - 1], opt);
      const namespace = namespaces[namespaces.length - 1];
      let nsSeparator = opt.nsSeparator !== void 0 ? opt.nsSeparator : this.options.nsSeparator;
      if (nsSeparator === void 0) nsSeparator = ":";
      const lng = opt.lng || this.language;
      const appendNamespaceToCIMode = opt.appendNamespaceToCIMode || this.options.appendNamespaceToCIMode;
      if (lng?.toLowerCase() === "cimode") {
        if (appendNamespaceToCIMode) {
          if (returnDetails) {
            return {
              res: `${namespace}${nsSeparator}${key}`,
              usedKey: key,
              exactUsedKey: key,
              usedLng: lng,
              usedNS: namespace,
              usedParams: this.getUsedParamsDetails(opt)
            };
          }
          return `${namespace}${nsSeparator}${key}`;
        }
        if (returnDetails) {
          return {
            res: key,
            usedKey: key,
            exactUsedKey: key,
            usedLng: lng,
            usedNS: namespace,
            usedParams: this.getUsedParamsDetails(opt)
          };
        }
        return key;
      }
      const resolved = this.resolve(keys, opt);
      let res = resolved?.res;
      const resUsedKey = resolved?.usedKey || key;
      const resExactUsedKey = resolved?.exactUsedKey || key;
      const noObject = ["[object Number]", "[object Function]", "[object RegExp]"];
      const joinArrays = opt.joinArrays !== void 0 ? opt.joinArrays : this.options.joinArrays;
      const handleAsObjectInI18nFormat = !this.i18nFormat || this.i18nFormat.handleAsObject;
      const needsPluralHandling = opt.count !== void 0 && !isString(opt.count);
      const hasDefaultValue = _Translator.hasDefaultValue(opt);
      const defaultValueSuffix = needsPluralHandling ? this.pluralResolver.getSuffix(lng, opt.count, opt) : "";
      const defaultValueSuffixOrdinalFallback = opt.ordinal && needsPluralHandling ? this.pluralResolver.getSuffix(lng, opt.count, {
        ordinal: false
      }) : "";
      const needsZeroSuffixLookup = needsPluralHandling && !opt.ordinal && opt.count === 0;
      const defaultValue = needsZeroSuffixLookup && opt[`defaultValue${this.options.pluralSeparator}zero`] || opt[`defaultValue${defaultValueSuffix}`] || opt[`defaultValue${defaultValueSuffixOrdinalFallback}`] || opt.defaultValue;
      let resForObjHndl = res;
      if (handleAsObjectInI18nFormat && !res && hasDefaultValue) {
        resForObjHndl = defaultValue;
      }
      const handleAsObject = shouldHandleAsObject(resForObjHndl);
      const resType = Object.prototype.toString.apply(resForObjHndl);
      if (handleAsObjectInI18nFormat && resForObjHndl && handleAsObject && noObject.indexOf(resType) < 0 && !(isString(joinArrays) && Array.isArray(resForObjHndl))) {
        if (!opt.returnObjects && !this.options.returnObjects) {
          if (!this.options.returnedObjectHandler) {
            this.logger.warn("accessing an object - but returnObjects options is not enabled!");
          }
          const r = this.options.returnedObjectHandler ? this.options.returnedObjectHandler(resUsedKey, resForObjHndl, {
            ...opt,
            ns: namespaces
          }) : `key '${key} (${this.language})' returned an object instead of string.`;
          if (returnDetails) {
            resolved.res = r;
            resolved.usedParams = this.getUsedParamsDetails(opt);
            return resolved;
          }
          return r;
        }
        if (keySeparator) {
          const resTypeIsArray = Array.isArray(resForObjHndl);
          const copy2 = resTypeIsArray ? [] : {};
          const newKeyToUse = resTypeIsArray ? resExactUsedKey : resUsedKey;
          for (const m in resForObjHndl) {
            if (Object.prototype.hasOwnProperty.call(resForObjHndl, m)) {
              const deepKey = `${newKeyToUse}${keySeparator}${m}`;
              if (hasDefaultValue && !res) {
                copy2[m] = this.translate(deepKey, {
                  ...opt,
                  defaultValue: shouldHandleAsObject(defaultValue) ? defaultValue[m] : void 0,
                  ...{
                    joinArrays: false,
                    ns: namespaces
                  }
                });
              } else {
                copy2[m] = this.translate(deepKey, {
                  ...opt,
                  ...{
                    joinArrays: false,
                    ns: namespaces
                  }
                });
              }
              if (copy2[m] === deepKey) copy2[m] = resForObjHndl[m];
            }
          }
          res = copy2;
        }
      } else if (handleAsObjectInI18nFormat && isString(joinArrays) && Array.isArray(res)) {
        res = res.join(joinArrays);
        if (res) res = this.extendTranslation(res, keys, opt, lastKey);
      } else {
        let usedDefault = false;
        let usedKey = false;
        if (!this.isValidLookup(res) && hasDefaultValue) {
          usedDefault = true;
          res = defaultValue;
        }
        if (!this.isValidLookup(res)) {
          usedKey = true;
          res = key;
        }
        const missingKeyNoValueFallbackToKey = opt.missingKeyNoValueFallbackToKey || this.options.missingKeyNoValueFallbackToKey;
        const resForMissing = missingKeyNoValueFallbackToKey && usedKey ? void 0 : res;
        const updateMissing = hasDefaultValue && defaultValue !== res && this.options.updateMissing;
        if (usedKey || usedDefault || updateMissing) {
          this.logger.log(updateMissing ? "updateKey" : "missingKey", lng, namespace, key, updateMissing ? defaultValue : res);
          if (keySeparator) {
            const fk = this.resolve(key, {
              ...opt,
              keySeparator: false
            });
            if (fk && fk.res) this.logger.warn("Seems the loaded translations were in flat JSON format instead of nested. Either set keySeparator: false on init or make sure your translations are published in nested format.");
          }
          let lngs = [];
          const fallbackLngs = this.languageUtils.getFallbackCodes(this.options.fallbackLng, opt.lng || this.language);
          if (this.options.saveMissingTo === "fallback" && fallbackLngs && fallbackLngs[0]) {
            for (let i = 0; i < fallbackLngs.length; i++) {
              lngs.push(fallbackLngs[i]);
            }
          } else if (this.options.saveMissingTo === "all") {
            lngs = this.languageUtils.toResolveHierarchy(opt.lng || this.language);
          } else {
            lngs.push(opt.lng || this.language);
          }
          const send = (l, k, specificDefaultValue) => {
            const defaultForMissing = hasDefaultValue && specificDefaultValue !== res ? specificDefaultValue : resForMissing;
            if (this.options.missingKeyHandler) {
              this.options.missingKeyHandler(l, namespace, k, defaultForMissing, updateMissing, opt);
            } else if (this.backendConnector?.saveMissing) {
              this.backendConnector.saveMissing(l, namespace, k, defaultForMissing, updateMissing, opt);
            }
            this.emit("missingKey", l, namespace, k, res);
          };
          if (this.options.saveMissing) {
            if (this.options.saveMissingPlurals && needsPluralHandling) {
              lngs.forEach((language) => {
                const suffixes = this.pluralResolver.getSuffixes(language, opt);
                if (needsZeroSuffixLookup && opt[`defaultValue${this.options.pluralSeparator}zero`] && suffixes.indexOf(`${this.options.pluralSeparator}zero`) < 0) {
                  suffixes.push(`${this.options.pluralSeparator}zero`);
                }
                suffixes.forEach((suffix) => {
                  send([language], key + suffix, opt[`defaultValue${suffix}`] || defaultValue);
                });
              });
            } else {
              send(lngs, key, defaultValue);
            }
          }
        }
        res = this.extendTranslation(res, keys, opt, resolved, lastKey);
        if (usedKey && res === key && this.options.appendNamespaceToMissingKey) {
          res = `${namespace}${nsSeparator}${key}`;
        }
        if ((usedKey || usedDefault) && this.options.parseMissingKeyHandler) {
          res = this.options.parseMissingKeyHandler(this.options.appendNamespaceToMissingKey ? `${namespace}${nsSeparator}${key}` : key, usedDefault ? res : void 0, opt);
        }
      }
      if (returnDetails) {
        resolved.res = res;
        resolved.usedParams = this.getUsedParamsDetails(opt);
        return resolved;
      }
      return res;
    }
    extendTranslation(res, key, opt, resolved, lastKey) {
      if (this.i18nFormat?.parse) {
        res = this.i18nFormat.parse(res, {
          ...this.options.interpolation.defaultVariables,
          ...opt
        }, opt.lng || this.language || resolved.usedLng, resolved.usedNS, resolved.usedKey, {
          resolved
        });
      } else if (!opt.skipInterpolation) {
        if (opt.interpolation) this.interpolator.init({
          ...opt,
          ...{
            interpolation: {
              ...this.options.interpolation,
              ...opt.interpolation
            }
          }
        });
        const skipOnVariables = isString(res) && (opt?.interpolation?.skipOnVariables !== void 0 ? opt.interpolation.skipOnVariables : this.options.interpolation.skipOnVariables);
        let nestBef;
        if (skipOnVariables) {
          const nb = res.match(this.interpolator.nestingRegexp);
          nestBef = nb && nb.length;
        }
        let data = opt.replace && !isString(opt.replace) ? opt.replace : opt;
        if (this.options.interpolation.defaultVariables) data = {
          ...this.options.interpolation.defaultVariables,
          ...data
        };
        res = this.interpolator.interpolate(res, data, opt.lng || this.language || resolved.usedLng, opt);
        if (skipOnVariables) {
          const na = res.match(this.interpolator.nestingRegexp);
          const nestAft = na && na.length;
          if (nestBef < nestAft) opt.nest = false;
        }
        if (!opt.lng && resolved && resolved.res) opt.lng = this.language || resolved.usedLng;
        if (opt.nest !== false) res = this.interpolator.nest(res, (...args) => {
          if (lastKey?.[0] === args[0] && !opt.context) {
            this.logger.warn(`It seems you are nesting recursively key: ${args[0]} in key: ${key[0]}`);
            return null;
          }
          return this.translate(...args, key);
        }, opt);
        if (opt.interpolation) this.interpolator.reset();
      }
      const postProcess = opt.postProcess || this.options.postProcess;
      const postProcessorNames = isString(postProcess) ? [postProcess] : postProcess;
      if (res != null && postProcessorNames?.length && opt.applyPostProcessor !== false) {
        res = postProcessor.handle(postProcessorNames, res, key, this.options && this.options.postProcessPassResolved ? {
          i18nResolved: {
            ...resolved,
            usedParams: this.getUsedParamsDetails(opt)
          },
          ...opt
        } : opt, this);
      }
      return res;
    }
    resolve(keys, opt = {}) {
      let found;
      let usedKey;
      let exactUsedKey;
      let usedLng;
      let usedNS;
      if (isString(keys)) keys = [keys];
      if (Array.isArray(keys)) keys = keys.map((k) => typeof k === "function" ? keysFromSelector(k, {
        ...this.options,
        ...opt
      }) : k);
      keys.forEach((k) => {
        if (this.isValidLookup(found)) return;
        const extracted = this.extractFromKey(k, opt);
        const key = extracted.key;
        usedKey = key;
        let namespaces = extracted.namespaces;
        if (this.options.fallbackNS) namespaces = namespaces.concat(this.options.fallbackNS);
        const needsPluralHandling = opt.count !== void 0 && !isString(opt.count);
        const needsZeroSuffixLookup = needsPluralHandling && !opt.ordinal && opt.count === 0;
        const needsContextHandling = opt.context !== void 0 && (isString(opt.context) || typeof opt.context === "number") && opt.context !== "";
        const codes = opt.lngs ? opt.lngs : this.languageUtils.toResolveHierarchy(opt.lng || this.language, opt.fallbackLng);
        namespaces.forEach((ns) => {
          if (this.isValidLookup(found)) return;
          usedNS = ns;
          if (!checkedLoadedFor[`${codes[0]}-${ns}`] && this.utils?.hasLoadedNamespace && !this.utils?.hasLoadedNamespace(usedNS)) {
            checkedLoadedFor[`${codes[0]}-${ns}`] = true;
            this.logger.warn(`key "${usedKey}" for languages "${codes.join(", ")}" won't get resolved as namespace "${usedNS}" was not yet loaded`, "This means something IS WRONG in your setup. You access the t function before i18next.init / i18next.loadNamespace / i18next.changeLanguage was done. Wait for the callback or Promise to resolve before accessing it!!!");
          }
          codes.forEach((code) => {
            if (this.isValidLookup(found)) return;
            usedLng = code;
            const finalKeys = [key];
            if (this.i18nFormat?.addLookupKeys) {
              this.i18nFormat.addLookupKeys(finalKeys, key, code, ns, opt);
            } else {
              let pluralSuffix;
              if (needsPluralHandling) pluralSuffix = this.pluralResolver.getSuffix(code, opt.count, opt);
              const zeroSuffix = `${this.options.pluralSeparator}zero`;
              const ordinalPrefix = `${this.options.pluralSeparator}ordinal${this.options.pluralSeparator}`;
              if (needsPluralHandling) {
                if (opt.ordinal && pluralSuffix.indexOf(ordinalPrefix) === 0) {
                  finalKeys.push(key + pluralSuffix.replace(ordinalPrefix, this.options.pluralSeparator));
                }
                finalKeys.push(key + pluralSuffix);
                if (needsZeroSuffixLookup) {
                  finalKeys.push(key + zeroSuffix);
                }
              }
              if (needsContextHandling) {
                const contextKey = `${key}${this.options.contextSeparator || "_"}${opt.context}`;
                finalKeys.push(contextKey);
                if (needsPluralHandling) {
                  if (opt.ordinal && pluralSuffix.indexOf(ordinalPrefix) === 0) {
                    finalKeys.push(contextKey + pluralSuffix.replace(ordinalPrefix, this.options.pluralSeparator));
                  }
                  finalKeys.push(contextKey + pluralSuffix);
                  if (needsZeroSuffixLookup) {
                    finalKeys.push(contextKey + zeroSuffix);
                  }
                }
              }
            }
            let possibleKey;
            while (possibleKey = finalKeys.pop()) {
              if (!this.isValidLookup(found)) {
                exactUsedKey = possibleKey;
                found = this.getResource(code, ns, possibleKey, opt);
              }
            }
          });
        });
      });
      return {
        res: found,
        usedKey,
        exactUsedKey,
        usedLng,
        usedNS
      };
    }
    isValidLookup(res) {
      return res !== void 0 && !(!this.options.returnNull && res === null) && !(!this.options.returnEmptyString && res === "");
    }
    getResource(code, ns, key, options = {}) {
      if (this.i18nFormat?.getResource) return this.i18nFormat.getResource(code, ns, key, options);
      return this.resourceStore.getResource(code, ns, key, options);
    }
    getUsedParamsDetails(options = {}) {
      const optionsKeys = ["defaultValue", "ordinal", "context", "replace", "lng", "lngs", "fallbackLng", "ns", "keySeparator", "nsSeparator", "returnObjects", "returnDetails", "joinArrays", "postProcess", "interpolation"];
      const useOptionsReplaceForData = options.replace && !isString(options.replace);
      let data = useOptionsReplaceForData ? options.replace : options;
      if (useOptionsReplaceForData && typeof options.count !== "undefined") {
        data.count = options.count;
      }
      if (this.options.interpolation.defaultVariables) {
        data = {
          ...this.options.interpolation.defaultVariables,
          ...data
        };
      }
      if (!useOptionsReplaceForData) {
        data = {
          ...data
        };
        for (const key of optionsKeys) {
          delete data[key];
        }
      }
      return data;
    }
    static hasDefaultValue(options) {
      const prefix = "defaultValue";
      for (const option in options) {
        if (Object.prototype.hasOwnProperty.call(options, option) && prefix === option.substring(0, prefix.length) && void 0 !== options[option]) {
          return true;
        }
      }
      return false;
    }
  };
  var LanguageUtil = class {
    constructor(options) {
      this.options = options;
      this.supportedLngs = this.options.supportedLngs || false;
      this.logger = baseLogger.create("languageUtils");
    }
    getScriptPartFromCode(code) {
      code = getCleanedCode(code);
      if (!code || code.indexOf("-") < 0) return null;
      const p = code.split("-");
      if (p.length === 2) return null;
      p.pop();
      if (p[p.length - 1].toLowerCase() === "x") return null;
      return this.formatLanguageCode(p.join("-"));
    }
    getLanguagePartFromCode(code) {
      code = getCleanedCode(code);
      if (!code || code.indexOf("-") < 0) return code;
      const p = code.split("-");
      return this.formatLanguageCode(p[0]);
    }
    formatLanguageCode(code) {
      if (isString(code) && code.indexOf("-") > -1) {
        let formattedCode;
        try {
          formattedCode = Intl.getCanonicalLocales(code)[0];
        } catch (e) {
        }
        if (formattedCode && this.options.lowerCaseLng) {
          formattedCode = formattedCode.toLowerCase();
        }
        if (formattedCode) return formattedCode;
        if (this.options.lowerCaseLng) {
          return code.toLowerCase();
        }
        return code;
      }
      return this.options.cleanCode || this.options.lowerCaseLng ? code.toLowerCase() : code;
    }
    isSupportedCode(code) {
      if (this.options.load === "languageOnly" || this.options.nonExplicitSupportedLngs) {
        code = this.getLanguagePartFromCode(code);
      }
      return !this.supportedLngs || !this.supportedLngs.length || this.supportedLngs.indexOf(code) > -1;
    }
    getBestMatchFromCodes(codes) {
      if (!codes) return null;
      let found;
      codes.forEach((code) => {
        if (found) return;
        const cleanedLng = this.formatLanguageCode(code);
        if (!this.options.supportedLngs || this.isSupportedCode(cleanedLng)) found = cleanedLng;
      });
      if (!found && this.options.supportedLngs) {
        codes.forEach((code) => {
          if (found) return;
          const lngScOnly = this.getScriptPartFromCode(code);
          if (this.isSupportedCode(lngScOnly)) return found = lngScOnly;
          const lngOnly = this.getLanguagePartFromCode(code);
          if (this.isSupportedCode(lngOnly)) return found = lngOnly;
          found = this.options.supportedLngs.find((supportedLng) => {
            if (supportedLng === lngOnly) return supportedLng;
            if (supportedLng.indexOf("-") < 0 && lngOnly.indexOf("-") < 0) return;
            if (supportedLng.indexOf("-") > 0 && lngOnly.indexOf("-") < 0 && supportedLng.substring(0, supportedLng.indexOf("-")) === lngOnly) return supportedLng;
            if (supportedLng.indexOf(lngOnly) === 0 && lngOnly.length > 1) return supportedLng;
          });
        });
      }
      if (!found) found = this.getFallbackCodes(this.options.fallbackLng)[0];
      return found;
    }
    getFallbackCodes(fallbacks, code) {
      if (!fallbacks) return [];
      if (typeof fallbacks === "function") fallbacks = fallbacks(code);
      if (isString(fallbacks)) fallbacks = [fallbacks];
      if (Array.isArray(fallbacks)) return fallbacks;
      if (!code) return fallbacks.default || [];
      let found = fallbacks[code];
      if (!found) found = fallbacks[this.getScriptPartFromCode(code)];
      if (!found) found = fallbacks[this.formatLanguageCode(code)];
      if (!found) found = fallbacks[this.getLanguagePartFromCode(code)];
      if (!found) found = fallbacks.default;
      return found || [];
    }
    toResolveHierarchy(code, fallbackCode) {
      const fallbackCodes = this.getFallbackCodes((fallbackCode === false ? [] : fallbackCode) || this.options.fallbackLng || [], code);
      const codes = [];
      const addCode = (c) => {
        if (!c) return;
        if (this.isSupportedCode(c)) {
          codes.push(c);
        } else {
          this.logger.warn(`rejecting language code not found in supportedLngs: ${c}`);
        }
      };
      if (isString(code) && (code.indexOf("-") > -1 || code.indexOf("_") > -1)) {
        if (this.options.load !== "languageOnly") addCode(this.formatLanguageCode(code));
        if (this.options.load !== "languageOnly" && this.options.load !== "currentOnly") addCode(this.getScriptPartFromCode(code));
        if (this.options.load !== "currentOnly") addCode(this.getLanguagePartFromCode(code));
      } else if (isString(code)) {
        addCode(this.formatLanguageCode(code));
      }
      fallbackCodes.forEach((fc) => {
        if (codes.indexOf(fc) < 0) addCode(this.formatLanguageCode(fc));
      });
      return codes;
    }
  };
  var suffixesOrder = {
    zero: 0,
    one: 1,
    two: 2,
    few: 3,
    many: 4,
    other: 5
  };
  var dummyRule = {
    select: (count) => count === 1 ? "one" : "other",
    resolvedOptions: () => ({
      pluralCategories: ["one", "other"]
    })
  };
  var PluralResolver = class {
    constructor(languageUtils, options = {}) {
      this.languageUtils = languageUtils;
      this.options = options;
      this.logger = baseLogger.create("pluralResolver");
      this.pluralRulesCache = {};
    }
    clearCache() {
      this.pluralRulesCache = {};
    }
    getRule(code, options = {}) {
      const cleanedCode = getCleanedCode(code === "dev" ? "en" : code);
      const type = options.ordinal ? "ordinal" : "cardinal";
      const cacheKey = JSON.stringify({
        cleanedCode,
        type
      });
      if (cacheKey in this.pluralRulesCache) {
        return this.pluralRulesCache[cacheKey];
      }
      let rule;
      try {
        rule = new Intl.PluralRules(cleanedCode, {
          type
        });
      } catch (err) {
        if (typeof Intl === "undefined") {
          this.logger.error("No Intl support, please use an Intl polyfill!");
          return dummyRule;
        }
        if (!code.match(/-|_/)) return dummyRule;
        const lngPart = this.languageUtils.getLanguagePartFromCode(code);
        rule = this.getRule(lngPart, options);
      }
      this.pluralRulesCache[cacheKey] = rule;
      return rule;
    }
    needsPlural(code, options = {}) {
      let rule = this.getRule(code, options);
      if (!rule) rule = this.getRule("dev", options);
      return rule?.resolvedOptions().pluralCategories.length > 1;
    }
    getPluralFormsOfKey(code, key, options = {}) {
      return this.getSuffixes(code, options).map((suffix) => `${key}${suffix}`);
    }
    getSuffixes(code, options = {}) {
      let rule = this.getRule(code, options);
      if (!rule) rule = this.getRule("dev", options);
      if (!rule) return [];
      return rule.resolvedOptions().pluralCategories.sort((pluralCategory1, pluralCategory2) => suffixesOrder[pluralCategory1] - suffixesOrder[pluralCategory2]).map((pluralCategory) => `${this.options.prepend}${options.ordinal ? `ordinal${this.options.prepend}` : ""}${pluralCategory}`);
    }
    getSuffix(code, count, options = {}) {
      const rule = this.getRule(code, options);
      if (rule) {
        return `${this.options.prepend}${options.ordinal ? `ordinal${this.options.prepend}` : ""}${rule.select(count)}`;
      }
      this.logger.warn(`no plural rule found for: ${code}`);
      return this.getSuffix("dev", count, options);
    }
  };
  var deepFindWithDefaults = (data, defaultData, key, keySeparator = ".", ignoreJSONStructure = true) => {
    let path = getPathWithDefaults(data, defaultData, key);
    if (!path && ignoreJSONStructure && isString(key)) {
      path = deepFind(data, key, keySeparator);
      if (path === void 0) path = deepFind(defaultData, key, keySeparator);
    }
    return path;
  };
  var regexSafe = (val) => val.replace(/\$/g, "$$$$");
  var Interpolator = class {
    constructor(options = {}) {
      this.logger = baseLogger.create("interpolator");
      this.options = options;
      this.format = options?.interpolation?.format || ((value) => value);
      this.init(options);
    }
    init(options = {}) {
      if (!options.interpolation) options.interpolation = {
        escapeValue: true
      };
      const {
        escape: escape$1,
        escapeValue,
        useRawValueToEscape,
        prefix,
        prefixEscaped,
        suffix,
        suffixEscaped,
        formatSeparator,
        unescapeSuffix,
        unescapePrefix,
        nestingPrefix,
        nestingPrefixEscaped,
        nestingSuffix,
        nestingSuffixEscaped,
        nestingOptionsSeparator,
        maxReplaces,
        alwaysFormat
      } = options.interpolation;
      this.escape = escape$1 !== void 0 ? escape$1 : escape;
      this.escapeValue = escapeValue !== void 0 ? escapeValue : true;
      this.useRawValueToEscape = useRawValueToEscape !== void 0 ? useRawValueToEscape : false;
      this.prefix = prefix ? regexEscape(prefix) : prefixEscaped || "{{";
      this.suffix = suffix ? regexEscape(suffix) : suffixEscaped || "}}";
      this.formatSeparator = formatSeparator || ",";
      this.unescapePrefix = unescapeSuffix ? "" : unescapePrefix || "-";
      this.unescapeSuffix = this.unescapePrefix ? "" : unescapeSuffix || "";
      this.nestingPrefix = nestingPrefix ? regexEscape(nestingPrefix) : nestingPrefixEscaped || regexEscape("$t(");
      this.nestingSuffix = nestingSuffix ? regexEscape(nestingSuffix) : nestingSuffixEscaped || regexEscape(")");
      this.nestingOptionsSeparator = nestingOptionsSeparator || ",";
      this.maxReplaces = maxReplaces || 1e3;
      this.alwaysFormat = alwaysFormat !== void 0 ? alwaysFormat : false;
      this.resetRegExp();
    }
    reset() {
      if (this.options) this.init(this.options);
    }
    resetRegExp() {
      const getOrResetRegExp = (existingRegExp, pattern) => {
        if (existingRegExp?.source === pattern) {
          existingRegExp.lastIndex = 0;
          return existingRegExp;
        }
        return new RegExp(pattern, "g");
      };
      this.regexp = getOrResetRegExp(this.regexp, `${this.prefix}(.+?)${this.suffix}`);
      this.regexpUnescape = getOrResetRegExp(this.regexpUnescape, `${this.prefix}${this.unescapePrefix}(.+?)${this.unescapeSuffix}${this.suffix}`);
      this.nestingRegexp = getOrResetRegExp(this.nestingRegexp, `${this.nestingPrefix}((?:[^()"']+|"[^"]*"|'[^']*'|\\((?:[^()]|"[^"]*"|'[^']*')*\\))*?)${this.nestingSuffix}`);
    }
    interpolate(str, data, lng, options) {
      let match;
      let value;
      let replaces;
      const defaultData = this.options && this.options.interpolation && this.options.interpolation.defaultVariables || {};
      const handleFormat = (key) => {
        if (key.indexOf(this.formatSeparator) < 0) {
          const path = deepFindWithDefaults(data, defaultData, key, this.options.keySeparator, this.options.ignoreJSONStructure);
          return this.alwaysFormat ? this.format(path, void 0, lng, {
            ...options,
            ...data,
            interpolationkey: key
          }) : path;
        }
        const p = key.split(this.formatSeparator);
        const k = p.shift().trim();
        const f = p.join(this.formatSeparator).trim();
        return this.format(deepFindWithDefaults(data, defaultData, k, this.options.keySeparator, this.options.ignoreJSONStructure), f, lng, {
          ...options,
          ...data,
          interpolationkey: k
        });
      };
      this.resetRegExp();
      const missingInterpolationHandler = options?.missingInterpolationHandler || this.options.missingInterpolationHandler;
      const skipOnVariables = options?.interpolation?.skipOnVariables !== void 0 ? options.interpolation.skipOnVariables : this.options.interpolation.skipOnVariables;
      const todos = [{
        regex: this.regexpUnescape,
        safeValue: (val) => regexSafe(val)
      }, {
        regex: this.regexp,
        safeValue: (val) => this.escapeValue ? regexSafe(this.escape(val)) : regexSafe(val)
      }];
      todos.forEach((todo) => {
        replaces = 0;
        while (match = todo.regex.exec(str)) {
          const matchedVar = match[1].trim();
          value = handleFormat(matchedVar);
          if (value === void 0) {
            if (typeof missingInterpolationHandler === "function") {
              const temp = missingInterpolationHandler(str, match, options);
              value = isString(temp) ? temp : "";
            } else if (options && Object.prototype.hasOwnProperty.call(options, matchedVar)) {
              value = "";
            } else if (skipOnVariables) {
              value = match[0];
              continue;
            } else {
              this.logger.warn(`missed to pass in variable ${matchedVar} for interpolating ${str}`);
              value = "";
            }
          } else if (!isString(value) && !this.useRawValueToEscape) {
            value = makeString(value);
          }
          const safeValue = todo.safeValue(value);
          str = str.replace(match[0], safeValue);
          if (skipOnVariables) {
            todo.regex.lastIndex += value.length;
            todo.regex.lastIndex -= match[0].length;
          } else {
            todo.regex.lastIndex = 0;
          }
          replaces++;
          if (replaces >= this.maxReplaces) {
            break;
          }
        }
      });
      return str;
    }
    nest(str, fc, options = {}) {
      let match;
      let value;
      let clonedOptions;
      const handleHasOptions = (key, inheritedOptions) => {
        const sep = this.nestingOptionsSeparator;
        if (key.indexOf(sep) < 0) return key;
        const c = key.split(new RegExp(`${regexEscape(sep)}[ ]*{`));
        let optionsString = `{${c[1]}`;
        key = c[0];
        optionsString = this.interpolate(optionsString, clonedOptions);
        const matchedSingleQuotes = optionsString.match(/'/g);
        const matchedDoubleQuotes = optionsString.match(/"/g);
        if ((matchedSingleQuotes?.length ?? 0) % 2 === 0 && !matchedDoubleQuotes || (matchedDoubleQuotes?.length ?? 0) % 2 !== 0) {
          optionsString = optionsString.replace(/'/g, '"');
        }
        try {
          clonedOptions = JSON.parse(optionsString);
          if (inheritedOptions) clonedOptions = {
            ...inheritedOptions,
            ...clonedOptions
          };
        } catch (e) {
          this.logger.warn(`failed parsing options string in nesting for key ${key}`, e);
          return `${key}${sep}${optionsString}`;
        }
        if (clonedOptions.defaultValue && clonedOptions.defaultValue.indexOf(this.prefix) > -1) delete clonedOptions.defaultValue;
        return key;
      };
      while (match = this.nestingRegexp.exec(str)) {
        let formatters = [];
        clonedOptions = {
          ...options
        };
        clonedOptions = clonedOptions.replace && !isString(clonedOptions.replace) ? clonedOptions.replace : clonedOptions;
        clonedOptions.applyPostProcessor = false;
        delete clonedOptions.defaultValue;
        const keyEndIndex = /{.*}/.test(match[1]) ? match[1].lastIndexOf("}") + 1 : match[1].indexOf(this.formatSeparator);
        if (keyEndIndex !== -1) {
          formatters = match[1].slice(keyEndIndex).split(this.formatSeparator).map((elem) => elem.trim()).filter(Boolean);
          match[1] = match[1].slice(0, keyEndIndex);
        }
        value = fc(handleHasOptions.call(this, match[1].trim(), clonedOptions), clonedOptions);
        if (value && match[0] === str && !isString(value)) return value;
        if (!isString(value)) value = makeString(value);
        if (!value) {
          this.logger.warn(`missed to resolve ${match[1]} for nesting ${str}`);
          value = "";
        }
        if (formatters.length) {
          value = formatters.reduce((v, f) => this.format(v, f, options.lng, {
            ...options,
            interpolationkey: match[1].trim()
          }), value.trim());
        }
        str = str.replace(match[0], value);
        this.regexp.lastIndex = 0;
      }
      return str;
    }
  };
  var parseFormatStr = (formatStr) => {
    let formatName = formatStr.toLowerCase().trim();
    const formatOptions = {};
    if (formatStr.indexOf("(") > -1) {
      const p = formatStr.split("(");
      formatName = p[0].toLowerCase().trim();
      const optStr = p[1].substring(0, p[1].length - 1);
      if (formatName === "currency" && optStr.indexOf(":") < 0) {
        if (!formatOptions.currency) formatOptions.currency = optStr.trim();
      } else if (formatName === "relativetime" && optStr.indexOf(":") < 0) {
        if (!formatOptions.range) formatOptions.range = optStr.trim();
      } else {
        const opts = optStr.split(";");
        opts.forEach((opt) => {
          if (opt) {
            const [key, ...rest] = opt.split(":");
            const val = rest.join(":").trim().replace(/^'+|'+$/g, "");
            const trimmedKey = key.trim();
            if (!formatOptions[trimmedKey]) formatOptions[trimmedKey] = val;
            if (val === "false") formatOptions[trimmedKey] = false;
            if (val === "true") formatOptions[trimmedKey] = true;
            if (!isNaN(val)) formatOptions[trimmedKey] = parseInt(val, 10);
          }
        });
      }
    }
    return {
      formatName,
      formatOptions
    };
  };
  var createCachedFormatter = (fn) => {
    const cache = {};
    return (v, l, o) => {
      let optForCache = o;
      if (o && o.interpolationkey && o.formatParams && o.formatParams[o.interpolationkey] && o[o.interpolationkey]) {
        optForCache = {
          ...optForCache,
          [o.interpolationkey]: void 0
        };
      }
      const key = l + JSON.stringify(optForCache);
      let frm = cache[key];
      if (!frm) {
        frm = fn(getCleanedCode(l), o);
        cache[key] = frm;
      }
      return frm(v);
    };
  };
  var createNonCachedFormatter = (fn) => (v, l, o) => fn(getCleanedCode(l), o)(v);
  var Formatter = class {
    constructor(options = {}) {
      this.logger = baseLogger.create("formatter");
      this.options = options;
      this.init(options);
    }
    init(services, options = {
      interpolation: {}
    }) {
      this.formatSeparator = options.interpolation.formatSeparator || ",";
      const cf = options.cacheInBuiltFormats ? createCachedFormatter : createNonCachedFormatter;
      this.formats = {
        number: cf((lng, opt) => {
          const formatter = new Intl.NumberFormat(lng, {
            ...opt
          });
          return (val) => formatter.format(val);
        }),
        currency: cf((lng, opt) => {
          const formatter = new Intl.NumberFormat(lng, {
            ...opt,
            style: "currency"
          });
          return (val) => formatter.format(val);
        }),
        datetime: cf((lng, opt) => {
          const formatter = new Intl.DateTimeFormat(lng, {
            ...opt
          });
          return (val) => formatter.format(val);
        }),
        relativetime: cf((lng, opt) => {
          const formatter = new Intl.RelativeTimeFormat(lng, {
            ...opt
          });
          return (val) => formatter.format(val, opt.range || "day");
        }),
        list: cf((lng, opt) => {
          const formatter = new Intl.ListFormat(lng, {
            ...opt
          });
          return (val) => formatter.format(val);
        })
      };
    }
    add(name, fc) {
      this.formats[name.toLowerCase().trim()] = fc;
    }
    addCached(name, fc) {
      this.formats[name.toLowerCase().trim()] = createCachedFormatter(fc);
    }
    format(value, format, lng, options = {}) {
      const formats = format.split(this.formatSeparator);
      if (formats.length > 1 && formats[0].indexOf("(") > 1 && formats[0].indexOf(")") < 0 && formats.find((f) => f.indexOf(")") > -1)) {
        const lastIndex = formats.findIndex((f) => f.indexOf(")") > -1);
        formats[0] = [formats[0], ...formats.splice(1, lastIndex)].join(this.formatSeparator);
      }
      const result = formats.reduce((mem, f) => {
        const {
          formatName,
          formatOptions
        } = parseFormatStr(f);
        if (this.formats[formatName]) {
          let formatted = mem;
          try {
            const valOptions = options?.formatParams?.[options.interpolationkey] || {};
            const l = valOptions.locale || valOptions.lng || options.locale || options.lng || lng;
            formatted = this.formats[formatName](mem, l, {
              ...formatOptions,
              ...options,
              ...valOptions
            });
          } catch (error) {
            this.logger.warn(error);
          }
          return formatted;
        } else {
          this.logger.warn(`there was no format function for ${formatName}`);
        }
        return mem;
      }, value);
      return result;
    }
  };
  var removePending = (q, name) => {
    if (q.pending[name] !== void 0) {
      delete q.pending[name];
      q.pendingCount--;
    }
  };
  var Connector = class extends EventEmitter {
    constructor(backend, store, services, options = {}) {
      super();
      this.backend = backend;
      this.store = store;
      this.services = services;
      this.languageUtils = services.languageUtils;
      this.options = options;
      this.logger = baseLogger.create("backendConnector");
      this.waitingReads = [];
      this.maxParallelReads = options.maxParallelReads || 10;
      this.readingCalls = 0;
      this.maxRetries = options.maxRetries >= 0 ? options.maxRetries : 5;
      this.retryTimeout = options.retryTimeout >= 1 ? options.retryTimeout : 350;
      this.state = {};
      this.queue = [];
      this.backend?.init?.(services, options.backend, options);
    }
    queueLoad(languages, namespaces, options, callback) {
      const toLoad = {};
      const pending = {};
      const toLoadLanguages = {};
      const toLoadNamespaces = {};
      languages.forEach((lng) => {
        let hasAllNamespaces = true;
        namespaces.forEach((ns) => {
          const name = `${lng}|${ns}`;
          if (!options.reload && this.store.hasResourceBundle(lng, ns)) {
            this.state[name] = 2;
          } else if (this.state[name] < 0) ;
          else if (this.state[name] === 1) {
            if (pending[name] === void 0) pending[name] = true;
          } else {
            this.state[name] = 1;
            hasAllNamespaces = false;
            if (pending[name] === void 0) pending[name] = true;
            if (toLoad[name] === void 0) toLoad[name] = true;
            if (toLoadNamespaces[ns] === void 0) toLoadNamespaces[ns] = true;
          }
        });
        if (!hasAllNamespaces) toLoadLanguages[lng] = true;
      });
      if (Object.keys(toLoad).length || Object.keys(pending).length) {
        this.queue.push({
          pending,
          pendingCount: Object.keys(pending).length,
          loaded: {},
          errors: [],
          callback
        });
      }
      return {
        toLoad: Object.keys(toLoad),
        pending: Object.keys(pending),
        toLoadLanguages: Object.keys(toLoadLanguages),
        toLoadNamespaces: Object.keys(toLoadNamespaces)
      };
    }
    loaded(name, err, data) {
      const s = name.split("|");
      const lng = s[0];
      const ns = s[1];
      if (err) this.emit("failedLoading", lng, ns, err);
      if (!err && data) {
        this.store.addResourceBundle(lng, ns, data, void 0, void 0, {
          skipCopy: true
        });
      }
      this.state[name] = err ? -1 : 2;
      if (err && data) this.state[name] = 0;
      const loaded = {};
      this.queue.forEach((q) => {
        pushPath(q.loaded, [lng], ns);
        removePending(q, name);
        if (err) q.errors.push(err);
        if (q.pendingCount === 0 && !q.done) {
          Object.keys(q.loaded).forEach((l) => {
            if (!loaded[l]) loaded[l] = {};
            const loadedKeys = q.loaded[l];
            if (loadedKeys.length) {
              loadedKeys.forEach((n) => {
                if (loaded[l][n] === void 0) loaded[l][n] = true;
              });
            }
          });
          q.done = true;
          if (q.errors.length) {
            q.callback(q.errors);
          } else {
            q.callback();
          }
        }
      });
      this.emit("loaded", loaded);
      this.queue = this.queue.filter((q) => !q.done);
    }
    read(lng, ns, fcName, tried = 0, wait = this.retryTimeout, callback) {
      if (!lng.length) return callback(null, {});
      if (this.readingCalls >= this.maxParallelReads) {
        this.waitingReads.push({
          lng,
          ns,
          fcName,
          tried,
          wait,
          callback
        });
        return;
      }
      this.readingCalls++;
      const resolver = (err, data) => {
        this.readingCalls--;
        if (this.waitingReads.length > 0) {
          const next = this.waitingReads.shift();
          this.read(next.lng, next.ns, next.fcName, next.tried, next.wait, next.callback);
        }
        if (err && data && tried < this.maxRetries) {
          setTimeout(() => {
            this.read.call(this, lng, ns, fcName, tried + 1, wait * 2, callback);
          }, wait);
          return;
        }
        callback(err, data);
      };
      const fc = this.backend[fcName].bind(this.backend);
      if (fc.length === 2) {
        try {
          const r = fc(lng, ns);
          if (r && typeof r.then === "function") {
            r.then((data) => resolver(null, data)).catch(resolver);
          } else {
            resolver(null, r);
          }
        } catch (err) {
          resolver(err);
        }
        return;
      }
      return fc(lng, ns, resolver);
    }
    prepareLoading(languages, namespaces, options = {}, callback) {
      if (!this.backend) {
        this.logger.warn("No backend was added via i18next.use. Will not load resources.");
        return callback && callback();
      }
      if (isString(languages)) languages = this.languageUtils.toResolveHierarchy(languages);
      if (isString(namespaces)) namespaces = [namespaces];
      const toLoad = this.queueLoad(languages, namespaces, options, callback);
      if (!toLoad.toLoad.length) {
        if (!toLoad.pending.length) callback();
        return null;
      }
      toLoad.toLoad.forEach((name) => {
        this.loadOne(name);
      });
    }
    load(languages, namespaces, callback) {
      this.prepareLoading(languages, namespaces, {}, callback);
    }
    reload(languages, namespaces, callback) {
      this.prepareLoading(languages, namespaces, {
        reload: true
      }, callback);
    }
    loadOne(name, prefix = "") {
      const s = name.split("|");
      const lng = s[0];
      const ns = s[1];
      this.read(lng, ns, "read", void 0, void 0, (err, data) => {
        if (err) this.logger.warn(`${prefix}loading namespace ${ns} for language ${lng} failed`, err);
        if (!err && data) this.logger.log(`${prefix}loaded namespace ${ns} for language ${lng}`, data);
        this.loaded(name, err, data);
      });
    }
    saveMissing(languages, namespace, key, fallbackValue, isUpdate, options = {}, clb = () => {
    }) {
      if (this.services?.utils?.hasLoadedNamespace && !this.services?.utils?.hasLoadedNamespace(namespace)) {
        this.logger.warn(`did not save key "${key}" as the namespace "${namespace}" was not yet loaded`, "This means something IS WRONG in your setup. You access the t function before i18next.init / i18next.loadNamespace / i18next.changeLanguage was done. Wait for the callback or Promise to resolve before accessing it!!!");
        return;
      }
      if (key === void 0 || key === null || key === "") return;
      if (this.backend?.create) {
        const opts = {
          ...options,
          isUpdate
        };
        const fc = this.backend.create.bind(this.backend);
        if (fc.length < 6) {
          try {
            let r;
            if (fc.length === 5) {
              r = fc(languages, namespace, key, fallbackValue, opts);
            } else {
              r = fc(languages, namespace, key, fallbackValue);
            }
            if (r && typeof r.then === "function") {
              r.then((data) => clb(null, data)).catch(clb);
            } else {
              clb(null, r);
            }
          } catch (err) {
            clb(err);
          }
        } else {
          fc(languages, namespace, key, fallbackValue, clb, opts);
        }
      }
      if (!languages || !languages[0]) return;
      this.store.addResource(languages[0], namespace, key, fallbackValue);
    }
  };
  var get = () => ({
    debug: false,
    initAsync: true,
    ns: ["translation"],
    defaultNS: ["translation"],
    fallbackLng: ["dev"],
    fallbackNS: false,
    supportedLngs: false,
    nonExplicitSupportedLngs: false,
    load: "all",
    preload: false,
    simplifyPluralSuffix: true,
    keySeparator: ".",
    nsSeparator: ":",
    pluralSeparator: "_",
    contextSeparator: "_",
    partialBundledLanguages: false,
    saveMissing: false,
    updateMissing: false,
    saveMissingTo: "fallback",
    saveMissingPlurals: true,
    missingKeyHandler: false,
    missingInterpolationHandler: false,
    postProcess: false,
    postProcessPassResolved: false,
    returnNull: false,
    returnEmptyString: true,
    returnObjects: false,
    joinArrays: false,
    returnedObjectHandler: false,
    parseMissingKeyHandler: false,
    appendNamespaceToMissingKey: false,
    appendNamespaceToCIMode: false,
    overloadTranslationOptionHandler: (args) => {
      let ret = {};
      if (typeof args[1] === "object") ret = args[1];
      if (isString(args[1])) ret.defaultValue = args[1];
      if (isString(args[2])) ret.tDescription = args[2];
      if (typeof args[2] === "object" || typeof args[3] === "object") {
        const options = args[3] || args[2];
        Object.keys(options).forEach((key) => {
          ret[key] = options[key];
        });
      }
      return ret;
    },
    interpolation: {
      escapeValue: true,
      format: (value) => value,
      prefix: "{{",
      suffix: "}}",
      formatSeparator: ",",
      unescapePrefix: "-",
      nestingPrefix: "$t(",
      nestingSuffix: ")",
      nestingOptionsSeparator: ",",
      maxReplaces: 1e3,
      skipOnVariables: true
    },
    cacheInBuiltFormats: true
  });
  var transformOptions = (options) => {
    if (isString(options.ns)) options.ns = [options.ns];
    if (isString(options.fallbackLng)) options.fallbackLng = [options.fallbackLng];
    if (isString(options.fallbackNS)) options.fallbackNS = [options.fallbackNS];
    if (options.supportedLngs?.indexOf?.("cimode") < 0) {
      options.supportedLngs = options.supportedLngs.concat(["cimode"]);
    }
    if (typeof options.initImmediate === "boolean") options.initAsync = options.initImmediate;
    return options;
  };
  var noop = () => {
  };
  var bindMemberFunctions = (inst) => {
    const mems = Object.getOwnPropertyNames(Object.getPrototypeOf(inst));
    mems.forEach((mem) => {
      if (typeof inst[mem] === "function") {
        inst[mem] = inst[mem].bind(inst);
      }
    });
  };
  var SUPPORT_NOTICE_KEY = "__i18next_supportNoticeShown";
  var getSupportNoticeShown = () => {
    if (typeof globalThis !== "undefined" && !!globalThis[SUPPORT_NOTICE_KEY]) return true;
    if (typeof process !== "undefined" && process.env && process.env.I18NEXT_NO_SUPPORT_NOTICE) return true;
    if (typeof process !== "undefined" && process.env && false) return true;
    return false;
  };
  var setSupportNoticeShown = () => {
    if (typeof globalThis !== "undefined") globalThis[SUPPORT_NOTICE_KEY] = true;
  };
  var usesLocize = (inst) => {
    if (inst?.modules?.backend?.name?.indexOf("Locize") > 0) return true;
    if (inst?.modules?.backend?.constructor?.name?.indexOf("Locize") > 0) return true;
    if (inst?.options?.backend?.backends) {
      if (inst.options.backend.backends.some((b) => b?.name?.indexOf("Locize") > 0 || b?.constructor?.name?.indexOf("Locize") > 0)) return true;
    }
    if (inst?.options?.backend?.projectId) return true;
    if (inst?.options?.backend?.backendOptions) {
      if (inst.options.backend.backendOptions.some((b) => b?.projectId)) return true;
    }
    return false;
  };
  var I18n = class _I18n extends EventEmitter {
    constructor(options = {}, callback) {
      super();
      this.options = transformOptions(options);
      this.services = {};
      this.logger = baseLogger;
      this.modules = {
        external: []
      };
      bindMemberFunctions(this);
      if (callback && !this.isInitialized && !options.isClone) {
        if (!this.options.initAsync) {
          this.init(options, callback);
          return this;
        }
        setTimeout(() => {
          this.init(options, callback);
        }, 0);
      }
    }
    init(options = {}, callback) {
      this.isInitializing = true;
      if (typeof options === "function") {
        callback = options;
        options = {};
      }
      if (options.defaultNS == null && options.ns) {
        if (isString(options.ns)) {
          options.defaultNS = options.ns;
        } else if (options.ns.indexOf("translation") < 0) {
          options.defaultNS = options.ns[0];
        }
      }
      const defOpts = get();
      this.options = {
        ...defOpts,
        ...this.options,
        ...transformOptions(options)
      };
      this.options.interpolation = {
        ...defOpts.interpolation,
        ...this.options.interpolation
      };
      if (options.keySeparator !== void 0) {
        this.options.userDefinedKeySeparator = options.keySeparator;
      }
      if (options.nsSeparator !== void 0) {
        this.options.userDefinedNsSeparator = options.nsSeparator;
      }
      if (typeof this.options.overloadTranslationOptionHandler !== "function") {
        this.options.overloadTranslationOptionHandler = defOpts.overloadTranslationOptionHandler;
      }
      if (this.options.showSupportNotice !== false && !usesLocize(this) && !getSupportNoticeShown()) {
        if (typeof console !== "undefined" && typeof console.info !== "undefined") console.info("🌐 i18next is made possible by our own product, Locize — consider powering your project with managed localization (AI, CDN, integrations): https://locize.com 💙");
        setSupportNoticeShown();
      }
      const createClassOnDemand = (ClassOrObject) => {
        if (!ClassOrObject) return null;
        if (typeof ClassOrObject === "function") return new ClassOrObject();
        return ClassOrObject;
      };
      if (!this.options.isClone) {
        if (this.modules.logger) {
          baseLogger.init(createClassOnDemand(this.modules.logger), this.options);
        } else {
          baseLogger.init(null, this.options);
        }
        let formatter;
        if (this.modules.formatter) {
          formatter = this.modules.formatter;
        } else {
          formatter = Formatter;
        }
        const lu = new LanguageUtil(this.options);
        this.store = new ResourceStore(this.options.resources, this.options);
        const s = this.services;
        s.logger = baseLogger;
        s.resourceStore = this.store;
        s.languageUtils = lu;
        s.pluralResolver = new PluralResolver(lu, {
          prepend: this.options.pluralSeparator,
          simplifyPluralSuffix: this.options.simplifyPluralSuffix
        });
        const usingLegacyFormatFunction = this.options.interpolation.format && this.options.interpolation.format !== defOpts.interpolation.format;
        if (usingLegacyFormatFunction) {
          this.logger.deprecate(`init: you are still using the legacy format function, please use the new approach: https://www.i18next.com/translation-function/formatting`);
        }
        if (formatter && (!this.options.interpolation.format || this.options.interpolation.format === defOpts.interpolation.format)) {
          s.formatter = createClassOnDemand(formatter);
          if (s.formatter.init) s.formatter.init(s, this.options);
          this.options.interpolation.format = s.formatter.format.bind(s.formatter);
        }
        s.interpolator = new Interpolator(this.options);
        s.utils = {
          hasLoadedNamespace: this.hasLoadedNamespace.bind(this)
        };
        s.backendConnector = new Connector(createClassOnDemand(this.modules.backend), s.resourceStore, s, this.options);
        s.backendConnector.on("*", (event, ...args) => {
          this.emit(event, ...args);
        });
        if (this.modules.languageDetector) {
          s.languageDetector = createClassOnDemand(this.modules.languageDetector);
          if (s.languageDetector.init) s.languageDetector.init(s, this.options.detection, this.options);
        }
        if (this.modules.i18nFormat) {
          s.i18nFormat = createClassOnDemand(this.modules.i18nFormat);
          if (s.i18nFormat.init) s.i18nFormat.init(this);
        }
        this.translator = new Translator(this.services, this.options);
        this.translator.on("*", (event, ...args) => {
          this.emit(event, ...args);
        });
        this.modules.external.forEach((m) => {
          if (m.init) m.init(this);
        });
      }
      this.format = this.options.interpolation.format;
      if (!callback) callback = noop;
      if (this.options.fallbackLng && !this.services.languageDetector && !this.options.lng) {
        const codes = this.services.languageUtils.getFallbackCodes(this.options.fallbackLng);
        if (codes.length > 0 && codes[0] !== "dev") this.options.lng = codes[0];
      }
      if (!this.services.languageDetector && !this.options.lng) {
        this.logger.warn("init: no languageDetector is used and no lng is defined");
      }
      const storeApi = ["getResource", "hasResourceBundle", "getResourceBundle", "getDataByLanguage"];
      storeApi.forEach((fcName) => {
        this[fcName] = (...args) => this.store[fcName](...args);
      });
      const storeApiChained = ["addResource", "addResources", "addResourceBundle", "removeResourceBundle"];
      storeApiChained.forEach((fcName) => {
        this[fcName] = (...args) => {
          this.store[fcName](...args);
          return this;
        };
      });
      const deferred = defer();
      const load = () => {
        const finish = (err, t2) => {
          this.isInitializing = false;
          if (this.isInitialized && !this.initializedStoreOnce) this.logger.warn("init: i18next is already initialized. You should call init just once!");
          this.isInitialized = true;
          if (!this.options.isClone) this.logger.log("initialized", this.options);
          this.emit("initialized", this.options);
          deferred.resolve(t2);
          callback(err, t2);
        };
        if (this.languages && !this.isInitialized) return finish(null, this.t.bind(this));
        this.changeLanguage(this.options.lng, finish);
      };
      if (this.options.resources || !this.options.initAsync) {
        load();
      } else {
        setTimeout(load, 0);
      }
      return deferred;
    }
    loadResources(language, callback = noop) {
      let usedCallback = callback;
      const usedLng = isString(language) ? language : this.language;
      if (typeof language === "function") usedCallback = language;
      if (!this.options.resources || this.options.partialBundledLanguages) {
        if (usedLng?.toLowerCase() === "cimode" && (!this.options.preload || this.options.preload.length === 0)) return usedCallback();
        const toLoad = [];
        const append = (lng) => {
          if (!lng) return;
          if (lng === "cimode") return;
          const lngs = this.services.languageUtils.toResolveHierarchy(lng);
          lngs.forEach((l) => {
            if (l === "cimode") return;
            if (toLoad.indexOf(l) < 0) toLoad.push(l);
          });
        };
        if (!usedLng) {
          const fallbacks = this.services.languageUtils.getFallbackCodes(this.options.fallbackLng);
          fallbacks.forEach((l) => append(l));
        } else {
          append(usedLng);
        }
        this.options.preload?.forEach?.((l) => append(l));
        this.services.backendConnector.load(toLoad, this.options.ns, (e) => {
          if (!e && !this.resolvedLanguage && this.language) this.setResolvedLanguage(this.language);
          usedCallback(e);
        });
      } else {
        usedCallback(null);
      }
    }
    reloadResources(lngs, ns, callback) {
      const deferred = defer();
      if (typeof lngs === "function") {
        callback = lngs;
        lngs = void 0;
      }
      if (typeof ns === "function") {
        callback = ns;
        ns = void 0;
      }
      if (!lngs) lngs = this.languages;
      if (!ns) ns = this.options.ns;
      if (!callback) callback = noop;
      this.services.backendConnector.reload(lngs, ns, (err) => {
        deferred.resolve();
        callback(err);
      });
      return deferred;
    }
    use(module) {
      if (!module) throw new Error("You are passing an undefined module! Please check the object you are passing to i18next.use()");
      if (!module.type) throw new Error("You are passing a wrong module! Please check the object you are passing to i18next.use()");
      if (module.type === "backend") {
        this.modules.backend = module;
      }
      if (module.type === "logger" || module.log && module.warn && module.error) {
        this.modules.logger = module;
      }
      if (module.type === "languageDetector") {
        this.modules.languageDetector = module;
      }
      if (module.type === "i18nFormat") {
        this.modules.i18nFormat = module;
      }
      if (module.type === "postProcessor") {
        postProcessor.addPostProcessor(module);
      }
      if (module.type === "formatter") {
        this.modules.formatter = module;
      }
      if (module.type === "3rdParty") {
        this.modules.external.push(module);
      }
      return this;
    }
    setResolvedLanguage(l) {
      if (!l || !this.languages) return;
      if (["cimode", "dev"].indexOf(l) > -1) return;
      for (let li = 0; li < this.languages.length; li++) {
        const lngInLngs = this.languages[li];
        if (["cimode", "dev"].indexOf(lngInLngs) > -1) continue;
        if (this.store.hasLanguageSomeTranslations(lngInLngs)) {
          this.resolvedLanguage = lngInLngs;
          break;
        }
      }
      if (!this.resolvedLanguage && this.languages.indexOf(l) < 0 && this.store.hasLanguageSomeTranslations(l)) {
        this.resolvedLanguage = l;
        this.languages.unshift(l);
      }
    }
    changeLanguage(lng, callback) {
      this.isLanguageChangingTo = lng;
      const deferred = defer();
      this.emit("languageChanging", lng);
      const setLngProps = (l) => {
        this.language = l;
        this.languages = this.services.languageUtils.toResolveHierarchy(l);
        this.resolvedLanguage = void 0;
        this.setResolvedLanguage(l);
      };
      const done = (err, l) => {
        if (l) {
          if (this.isLanguageChangingTo === lng) {
            setLngProps(l);
            this.translator.changeLanguage(l);
            this.isLanguageChangingTo = void 0;
            this.emit("languageChanged", l);
            this.logger.log("languageChanged", l);
          }
        } else {
          this.isLanguageChangingTo = void 0;
        }
        deferred.resolve((...args) => this.t(...args));
        if (callback) callback(err, (...args) => this.t(...args));
      };
      const setLng = (lngs) => {
        if (!lng && !lngs && this.services.languageDetector) lngs = [];
        const fl = isString(lngs) ? lngs : lngs && lngs[0];
        const l = this.store.hasLanguageSomeTranslations(fl) ? fl : this.services.languageUtils.getBestMatchFromCodes(isString(lngs) ? [lngs] : lngs);
        if (l) {
          if (!this.language) {
            setLngProps(l);
          }
          if (!this.translator.language) this.translator.changeLanguage(l);
          this.services.languageDetector?.cacheUserLanguage?.(l);
        }
        this.loadResources(l, (err) => {
          done(err, l);
        });
      };
      if (!lng && this.services.languageDetector && !this.services.languageDetector.async) {
        setLng(this.services.languageDetector.detect());
      } else if (!lng && this.services.languageDetector && this.services.languageDetector.async) {
        if (this.services.languageDetector.detect.length === 0) {
          this.services.languageDetector.detect().then(setLng);
        } else {
          this.services.languageDetector.detect(setLng);
        }
      } else {
        setLng(lng);
      }
      return deferred;
    }
    getFixedT(lng, ns, keyPrefix) {
      const fixedT = (key, opts, ...rest) => {
        let o;
        if (typeof opts !== "object") {
          o = this.options.overloadTranslationOptionHandler([key, opts].concat(rest));
        } else {
          o = {
            ...opts
          };
        }
        o.lng = o.lng || fixedT.lng;
        o.lngs = o.lngs || fixedT.lngs;
        o.ns = o.ns || fixedT.ns;
        if (o.keyPrefix !== "") o.keyPrefix = o.keyPrefix || keyPrefix || fixedT.keyPrefix;
        const selectorOpts = {
          ...this.options,
          ...o
        };
        if (typeof o.keyPrefix === "function") o.keyPrefix = keysFromSelector(o.keyPrefix, selectorOpts);
        const keySeparator = this.options.keySeparator || ".";
        let resultKey;
        if (o.keyPrefix && Array.isArray(key)) {
          resultKey = key.map((k) => {
            if (typeof k === "function") k = keysFromSelector(k, selectorOpts);
            return `${o.keyPrefix}${keySeparator}${k}`;
          });
        } else {
          if (typeof key === "function") key = keysFromSelector(key, selectorOpts);
          resultKey = o.keyPrefix ? `${o.keyPrefix}${keySeparator}${key}` : key;
        }
        return this.t(resultKey, o);
      };
      if (isString(lng)) {
        fixedT.lng = lng;
      } else {
        fixedT.lngs = lng;
      }
      fixedT.ns = ns;
      fixedT.keyPrefix = keyPrefix;
      return fixedT;
    }
    t(...args) {
      return this.translator?.translate(...args);
    }
    exists(...args) {
      return this.translator?.exists(...args);
    }
    setDefaultNamespace(ns) {
      this.options.defaultNS = ns;
    }
    hasLoadedNamespace(ns, options = {}) {
      if (!this.isInitialized) {
        this.logger.warn("hasLoadedNamespace: i18next was not initialized", this.languages);
        return false;
      }
      if (!this.languages || !this.languages.length) {
        this.logger.warn("hasLoadedNamespace: i18n.languages were undefined or empty", this.languages);
        return false;
      }
      const lng = options.lng || this.resolvedLanguage || this.languages[0];
      const fallbackLng = this.options ? this.options.fallbackLng : false;
      const lastLng = this.languages[this.languages.length - 1];
      if (lng.toLowerCase() === "cimode") return true;
      const loadNotPending = (l, n) => {
        const loadState = this.services.backendConnector.state[`${l}|${n}`];
        return loadState === -1 || loadState === 0 || loadState === 2;
      };
      if (options.precheck) {
        const preResult = options.precheck(this, loadNotPending);
        if (preResult !== void 0) return preResult;
      }
      if (this.hasResourceBundle(lng, ns)) return true;
      if (!this.services.backendConnector.backend || this.options.resources && !this.options.partialBundledLanguages) return true;
      if (loadNotPending(lng, ns) && (!fallbackLng || loadNotPending(lastLng, ns))) return true;
      return false;
    }
    loadNamespaces(ns, callback) {
      const deferred = defer();
      if (!this.options.ns) {
        if (callback) callback();
        return Promise.resolve();
      }
      if (isString(ns)) ns = [ns];
      ns.forEach((n) => {
        if (this.options.ns.indexOf(n) < 0) this.options.ns.push(n);
      });
      this.loadResources((err) => {
        deferred.resolve();
        if (callback) callback(err);
      });
      return deferred;
    }
    loadLanguages(lngs, callback) {
      const deferred = defer();
      if (isString(lngs)) lngs = [lngs];
      const preloaded = this.options.preload || [];
      const newLngs = lngs.filter((lng) => preloaded.indexOf(lng) < 0 && this.services.languageUtils.isSupportedCode(lng));
      if (!newLngs.length) {
        if (callback) callback();
        return Promise.resolve();
      }
      this.options.preload = preloaded.concat(newLngs);
      this.loadResources((err) => {
        deferred.resolve();
        if (callback) callback(err);
      });
      return deferred;
    }
    dir(lng) {
      if (!lng) lng = this.resolvedLanguage || (this.languages?.length > 0 ? this.languages[0] : this.language);
      if (!lng) return "rtl";
      try {
        const l = new Intl.Locale(lng);
        if (l && l.getTextInfo) {
          const ti = l.getTextInfo();
          if (ti && ti.direction) return ti.direction;
        }
      } catch (e) {
      }
      const rtlLngs = ["ar", "shu", "sqr", "ssh", "xaa", "yhd", "yud", "aao", "abh", "abv", "acm", "acq", "acw", "acx", "acy", "adf", "ads", "aeb", "aec", "afb", "ajp", "apc", "apd", "arb", "arq", "ars", "ary", "arz", "auz", "avl", "ayh", "ayl", "ayn", "ayp", "bbz", "pga", "he", "iw", "ps", "pbt", "pbu", "pst", "prp", "prd", "ug", "ur", "ydd", "yds", "yih", "ji", "yi", "hbo", "men", "xmn", "fa", "jpr", "peo", "pes", "prs", "dv", "sam", "ckb"];
      const languageUtils = this.services?.languageUtils || new LanguageUtil(get());
      if (lng.toLowerCase().indexOf("-latn") > 1) return "ltr";
      return rtlLngs.indexOf(languageUtils.getLanguagePartFromCode(lng)) > -1 || lng.toLowerCase().indexOf("-arab") > 1 ? "rtl" : "ltr";
    }
    static createInstance(options = {}, callback) {
      const instance2 = new _I18n(options, callback);
      instance2.createInstance = _I18n.createInstance;
      return instance2;
    }
    cloneInstance(options = {}, callback = noop) {
      const forkResourceStore = options.forkResourceStore;
      if (forkResourceStore) delete options.forkResourceStore;
      const mergedOptions = {
        ...this.options,
        ...options,
        ...{
          isClone: true
        }
      };
      const clone = new _I18n(mergedOptions);
      if (options.debug !== void 0 || options.prefix !== void 0) {
        clone.logger = clone.logger.clone(options);
      }
      const membersToCopy = ["store", "services", "language"];
      membersToCopy.forEach((m) => {
        clone[m] = this[m];
      });
      clone.services = {
        ...this.services
      };
      clone.services.utils = {
        hasLoadedNamespace: clone.hasLoadedNamespace.bind(clone)
      };
      if (forkResourceStore) {
        const clonedData = Object.keys(this.store.data).reduce((prev, l) => {
          prev[l] = {
            ...this.store.data[l]
          };
          prev[l] = Object.keys(prev[l]).reduce((acc, n) => {
            acc[n] = {
              ...prev[l][n]
            };
            return acc;
          }, prev[l]);
          return prev;
        }, {});
        clone.store = new ResourceStore(clonedData, mergedOptions);
        clone.services.resourceStore = clone.store;
      }
      if (options.interpolation) {
        const defOpts = get();
        const mergedInterpolation = {
          ...defOpts.interpolation,
          ...this.options.interpolation,
          ...options.interpolation
        };
        const mergedForInterpolator = {
          ...mergedOptions,
          interpolation: mergedInterpolation
        };
        clone.services.interpolator = new Interpolator(mergedForInterpolator);
      }
      clone.translator = new Translator(clone.services, mergedOptions);
      clone.translator.on("*", (event, ...args) => {
        clone.emit(event, ...args);
      });
      clone.init(mergedOptions, callback);
      clone.translator.options = mergedOptions;
      clone.translator.backendConnector.services.utils = {
        hasLoadedNamespace: clone.hasLoadedNamespace.bind(clone)
      };
      return clone;
    }
    toJSON() {
      return {
        options: this.options,
        store: this.store,
        language: this.language,
        languages: this.languages,
        resolvedLanguage: this.resolvedLanguage
      };
    }
  };
  var instance = I18n.createInstance();
  var createInstance = instance.createInstance;
  var dir = instance.dir;
  var init = instance.init;
  var loadResources = instance.loadResources;
  var reloadResources = instance.reloadResources;
  var use = instance.use;
  var changeLanguage = instance.changeLanguage;
  var getFixedT = instance.getFixedT;
  var t = instance.t;
  var exists = instance.exists;
  var setDefaultNamespace = instance.setDefaultNamespace;
  var hasLoadedNamespace = instance.hasLoadedNamespace;
  var loadNamespaces = instance.loadNamespaces;
  var loadLanguages = instance.loadLanguages;

  // vos.Tendril/src/types/dashboard.ts
  var DASHBOARD_SPECIFICATION_PROPERTY = "specification";
  var SCOPE_REFERENCE = "$scope";
  var IS_PREDICATE = "is";
  var UTC_OFFSET_PROPERTY = "utcOffsetSeconds";

  // vos.Tendril/src/types/vos.ts
  var DECLARED_WRITE_KINDS = ["FactOnly", "ObservationOnly"];

  // vos.Tendril/src/utils/precedenceOrder.ts
  function inPrecedenceOrder(a, b) {
    return a < b ? -1 : a > b ? 1 : 0;
  }

  // vos.Tendril/src/utils/propertyMapper.ts
  function isDeclaredWriteKind(value) {
    return DECLARED_WRITE_KINDS.includes(value);
  }
  function unwrapPropertyValue(v) {
    if (v !== null && typeof v === "object" && "value" in v) {
      return v.value;
    }
    return v;
  }
  function declaredWriteKinds(props) {
    if (!props) return void 0;
    const kinds = {};
    for (const [key, value] of Object.entries(props)) {
      if (value === null || typeof value !== "object") continue;
      const declared = value.writeKind;
      if (isDeclaredWriteKind(declared)) kinds[key] = declared;
    }
    return Object.keys(kinds).length ? kinds : void 0;
  }
  function unwrapProperties(props) {
    if (!props) return {};
    const result = {};
    for (const [key, value] of Object.entries(props)) {
      result[key] = unwrapPropertyValue(value);
    }
    return result;
  }
  function unwrapInheritedPropertySet(ips) {
    if (!ips) return void 0;
    const result = {};
    for (const [key, set] of Object.entries(ips)) {
      result[key] = {
        ...set,
        Properties: unwrapProperties(set.Properties),
        PropertyWriteKinds: declaredWriteKinds(set.Properties),
        Inherited: unwrapInheritedPropertySet(set.Inherited)
      };
    }
    return result;
  }
  function unwrapThing(thing) {
    return {
      ...thing,
      Properties: unwrapProperties(thing.Properties),
      PropertyWriteKinds: declaredWriteKinds(thing.Properties),
      InheritedOverrides: unwrapInheritedPropertySet(thing.InheritedOverrides)
    };
  }
  var effectivePropertiesCache = /* @__PURE__ */ new WeakMap();
  function collectOverrides(sets, merged) {
    if (!sets) return;
    const ordered = Object.values(sets).sort((a, b) => inPrecedenceOrder(a.SourceName, b.SourceName));
    for (const set of ordered) {
      collectOverrides(set.Inherited, merged);
      Object.assign(merged, set.Properties);
    }
  }
  function effectiveProperties(thing, lookup) {
    return resolveEffective(thing, lookup, /* @__PURE__ */ new Set());
  }
  function resolveEffective(thing, lookup, visiting) {
    let perModel = effectivePropertiesCache.get(lookup);
    if (!perModel) {
      perModel = /* @__PURE__ */ new WeakMap();
      effectivePropertiesCache.set(lookup, perModel);
    }
    const cached = perModel.get(thing);
    if (cached) return cached;
    const merged = {};
    foldInAncestors(
      thing,
      lookup,
      visiting,
      (parent) => Object.assign(merged, resolveEffective(parent, lookup, visiting))
    );
    collectOverrides(thing.InheritedOverrides, merged);
    Object.assign(merged, thing.Properties);
    const frozen = Object.freeze(merged);
    perModel.set(thing, frozen);
    return frozen;
  }
  function foldInAncestors(thing, lookup, visiting, fold) {
    const parents = (thing.Id ? lookup.isParents.get(thing.Id) ?? [] : []).map((id) => lookup.byId.get(id)).filter((p) => !!p && !visiting.has(p.Id)).sort((a, b) => inPrecedenceOrder(a.Name, b.Name));
    if (!parents.length || !thing.Id) return;
    visiting.add(thing.Id);
    for (const parent of parents) fold(parent);
    visiting.delete(thing.Id);
  }
  function effectiveDerivedDefinitions(thing, lookup) {
    return resolveDefinitions(thing, lookup, /* @__PURE__ */ new Set());
  }
  function resolveDefinitions(thing, lookup, visiting) {
    const merged = {};
    foldInAncestors(
      thing,
      lookup,
      visiting,
      (parent) => Object.assign(merged, resolveDefinitions(parent, lookup, visiting))
    );
    Object.assign(merged, thing.DerivedProperties);
    return merged;
  }
  function unwrapRelationship(relationship) {
    return {
      ...relationship,
      Properties: unwrapProperties(relationship.Properties)
    };
  }

  // vos.Tendril/src/utils/propertyOrigin.ts
  function valueOrigin(thing, property, lookup) {
    const source = statedBy(thing, property, lookup, /* @__PURE__ */ new Set());
    if (!source) return { origin: "unknown", assumedFrom: null };
    if (source !== thing) return { origin: "assumed", assumedFrom: source.Name };
    switch (declaredWriteKind(thing, property, lookup, /* @__PURE__ */ new Set())) {
      case "FactOnly":
        return { origin: "stated", assumedFrom: null };
      case "ObservationOnly":
        return { origin: "measured", assumedFrom: null };
      default:
        return { origin: "unknown", assumedFrom: null };
    }
  }
  function statedBy(thing, property, lookup, visiting) {
    if (storedValue(thing, property) != null) return thing;
    if (visiting.has(thing.Id)) return null;
    visiting.add(thing.Id);
    for (const parent of ancestorsInPrecedenceOrder(thing, lookup)) {
      const supplied = statedBy(parent, property, lookup, visiting);
      if (supplied) return supplied;
    }
    return null;
  }
  function declaredWriteKind(thing, property, lookup, visiting) {
    const here = storedWriteKind(thing, property);
    if (here) return here;
    if (visiting.has(thing.Id)) return void 0;
    visiting.add(thing.Id);
    for (const parent of ancestorsInPrecedenceOrder(thing, lookup)) {
      const declared = declaredWriteKind(parent, property, lookup, visiting);
      if (declared) return declared;
    }
    return void 0;
  }
  function ancestorsInPrecedenceOrder(thing, lookup) {
    return (lookup.isParents.get(thing.Id) ?? []).map((id) => lookup.byId.get(id)).filter((parent) => !!parent).sort((a, b) => inPrecedenceOrder(b.Name, a.Name));
  }
  function storedValue(thing, property) {
    const own = thing.Properties?.[property];
    if (own != null) return own;
    return firstInStoredSets(thing.InheritedOverrides, (set) => set.Properties?.[property]);
  }
  function storedWriteKind(thing, property) {
    return thing.PropertyWriteKinds?.[property] ?? firstInStoredSets(thing.InheritedOverrides, (set) => set.PropertyWriteKinds?.[property]);
  }
  function firstInStoredSets(sets, read) {
    for (const set of Object.values(sets ?? {})) {
      const answer = read(set);
      if (answer != null) return answer;
      const nested = firstInStoredSets(set.Inherited, read);
      if (nested != null) return nested;
    }
    return void 0;
  }

  // vos.Tendril/src/utils/rangeHelpers.ts
  function findRange(name, data) {
    const own = data.OwnRanges.find((r) => r.Name === name);
    if (own) return own;
    function walk(sets) {
      for (const s of sets) {
        const found = s.Ranges.find((r) => r.Name === name);
        if (found) return found;
        const nested = walk(s.Inherited);
        if (nested) return nested;
      }
      return void 0;
    }
    return walk(data.InheritedRanges);
  }

  // vos.Tendril/src/i18n/words.ts
  var TENDRIL_NAMESPACE = "tendril";
  var en = {
    stateCountMissing: "The number of things in “{{state}}” could not be read.",
    showDecided: "Show decided",
    cancel: "Cancel",
    cannotRelate: "This page cannot relate a row along “{{predicate}}”.",
    rosterUnreadable: "The list to choose from could not be read.",
    cannotPress: "This page cannot send changes."
  };
  var TENDRIL_WORDS = {
    en,
    de: { stateCountMissing: "Die Anzahl der Dinge in „{{state}}“ konnte nicht gelesen werden.", showDecided: "Entschiedene zeigen", cancel: "Abbrechen", cannotRelate: "Diese Seite kann eine Zeile nicht über „{{predicate}}“ verknüpfen.", rosterUnreadable: "Die Liste zur Auswahl konnte nicht gelesen werden.", cannotPress: "Diese Seite kann keine Änderungen senden." },
    es: { stateCountMissing: "No se ha podido leer el número de cosas en «{{state}}».", showDecided: "Mostrar decididos", cancel: "Cancelar", cannotRelate: "Esta página no puede relacionar una fila mediante «{{predicate}}».", rosterUnreadable: "No se ha podido leer la lista para elegir.", cannotPress: "Esta página no puede enviar cambios." },
    fr: { stateCountMissing: "Le nombre de choses dans « {{state}} » n’a pas pu être lu.", showDecided: "Afficher les décidés", cancel: "Annuler", cannotRelate: "Cette page ne peut pas relier une ligne par « {{predicate}} ».", rosterUnreadable: "La liste où choisir n’a pas pu être lue.", cannotPress: "Cette page ne peut pas envoyer de modifications." },
    it: { stateCountMissing: "Impossibile leggere il numero di cose in «{{state}}».", showDecided: "Mostra decisi", cancel: "Annulla", cannotRelate: "Questa pagina non può collegare una riga tramite «{{predicate}}».", rosterUnreadable: "Impossibile leggere l’elenco da cui scegliere.", cannotPress: "Questa pagina non può inviare modifiche." },
    nl: { stateCountMissing: "Het aantal dingen in ‘{{state}}’ kon niet worden gelezen.", showDecided: "Besliste tonen", cancel: "Annuleren", cannotRelate: "Deze pagina kan een rij niet verbinden via ‘{{predicate}}’.", rosterUnreadable: "De lijst om uit te kiezen kon niet worden gelezen.", cannotPress: "Deze pagina kan geen wijzigingen verzenden." },
    ar: { stateCountMissing: "تعذّرت قراءة عدد الأشياء في «{{state}}».", showDecided: "إظهار ما تقرّر", cancel: "إلغاء", cannotRelate: "لا تستطيع هذه الصفحة ربط صف عبر «{{predicate}}».", rosterUnreadable: "تعذّرت قراءة القائمة للاختيار منها.", cannotPress: "لا تستطيع هذه الصفحة إرسال تغييرات." }
  };
  function tendrilWord(key, values) {
    return instance.t(key, { ...values, ns: TENDRIL_NAMESPACE });
  }

  // vos.Tendril/src/i18n/languages.ts
  var BASE_LANGUAGE = "en";

  // vos.Tendril/src/i18n/termWording.ts
  function wordingIn(stated) {
    if (typeof stated !== "string") return null;
    try {
      const parsed = JSON.parse(stated);
      if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) return null;
      const words = Object.entries(parsed).filter((entry) => typeof entry[1] === "string");
      return words.length > 0 ? Object.fromEntries(words) : null;
    } catch {
      return null;
    }
  }

  // vos.Tendril/src/api/compareNames.ts
  var collators = /* @__PURE__ */ new Map();
  function compareNamesIn(language) {
    let collator = collators.get(language);
    if (!collator) {
      collator = new Intl.Collator(language);
      collators.set(language, collator);
    }
    return collator.compare;
  }

  // vos.Tendril/src/api/platformRoles.ts
  var BINDS_PREDICATE_NAME = "binds";
  var PAGE_ROLE_ID = "a18768bc-b4e7-513c-864f-842d674c1858";
  var DISPLAY_SETTINGS_ROLE_ID = "1cb6206a-2726-5008-8188-698b299f5665";
  var PREDICATE_ROLE_ID = "86743302-6588-5a93-b6a5-0dde78b092c1";

  // vos.Tendril/src/api/propertyNames.ts
  var PROPERTY_NAME_ROLE = "a44ad23e-44ed-5992-8360-1fdabc1a6240";
  var NAMED_PROPERTY = "namedProperty";
  var PROPERTY_NAME_WORDING = "wording";
  var NAMES_PROPERTY_OF_PREDICATE = "namesPropertyOf";

  // vos.Tendril/src/api/dashboardApi.ts
  function declaredPredicates(relationships, predicateNameToId, isChildren, byId) {
    const bindsId = predicateNameToId.get(BINDS_PREDICATE_NAME);
    const root = relationships.find((r) => r.SubjectId === PREDICATE_ROLE_ID && r.PredicateId === bindsId)?.TargetId;
    if (!bindsId || !root) return [];
    const found = [];
    const seen = /* @__PURE__ */ new Set([root]);
    const waiting = [...isChildren.get(root) ?? []];
    while (waiting.length > 0) {
      const id = waiting.pop();
      if (seen.has(id)) continue;
      seen.add(id);
      const held2 = byId.get(id);
      if (held2) found.push(held2);
      waiting.push(...isChildren.get(id) ?? []);
    }
    return found;
  }
  function buildModelIndex(things, relationships) {
    const byId = /* @__PURE__ */ new Map();
    const byName = /* @__PURE__ */ new Map();
    const otherTypesByName = /* @__PURE__ */ new Map();
    const archetypeIds = /* @__PURE__ */ new Set();
    for (const t2 of things) {
      byId.set(t2.Id, t2);
      if (!byName.has(t2.Name)) byName.set(t2.Name, t2);
      else if (t2.IsArchetype) {
        const others = otherTypesByName.get(t2.Name);
        if (others) others.push(t2.Id);
        else otherTypesByName.set(t2.Name, [t2.Id]);
      }
      if (t2.IsArchetype) archetypeIds.add(t2.Id);
    }
    const predicateNameToId = /* @__PURE__ */ new Map();
    const predicateIdToName = /* @__PURE__ */ new Map();
    const relationshipsByPredicate = /* @__PURE__ */ new Map();
    for (const r of relationships) {
      const asserted = relationshipsByPredicate.get(r.PredicateId);
      if (asserted) asserted.push(r);
      else relationshipsByPredicate.set(r.PredicateId, [r]);
      const p = byId.get(r.PredicateId);
      if (p) {
        predicateIdToName.set(r.PredicateId, p.Name);
        if (!predicateNameToId.has(p.Name)) predicateNameToId.set(p.Name, r.PredicateId);
      }
    }
    const isId = predicateNameToId.get(IS_PREDICATE);
    const isChildren = /* @__PURE__ */ new Map();
    const isParents = /* @__PURE__ */ new Map();
    if (isId) {
      for (const r of relationships) {
        if (r.PredicateId !== isId) continue;
        const kids = isChildren.get(r.TargetId);
        if (kids) kids.push(r.SubjectId);
        else isChildren.set(r.TargetId, [r.SubjectId]);
        const parents = isParents.get(r.SubjectId);
        if (parents) parents.push(r.TargetId);
        else isParents.set(r.SubjectId, [r.TargetId]);
      }
    }
    for (const predicate of declaredPredicates(relationships, predicateNameToId, isChildren, byId)) {
      predicateIdToName.set(predicate.Id, predicate.Name);
      if (!predicateNameToId.has(predicate.Name)) predicateNameToId.set(predicate.Name, predicate.Id);
    }
    return {
      byId,
      byName,
      otherTypesByName,
      relationships,
      relationshipsByPredicate,
      predicateNameToId,
      predicateIdToName,
      isChildren,
      archetypeIds,
      isParents,
      archetypeMembers: /* @__PURE__ */ new Map(),
      adjacencyByPredicate: /* @__PURE__ */ new Map(),
      dashboards: /* @__PURE__ */ new Map(),
      propertyNames: null
    };
  }
  function adjacency(predicateId, inbound, index) {
    const key = `${predicateId}:${inbound ? "in" : "out"}`;
    const built = index.adjacencyByPredicate.get(key);
    if (built) return built;
    const edges = /* @__PURE__ */ new Map();
    for (const r of index.relationshipsByPredicate.get(predicateId) ?? []) {
      const [from, to] = inbound ? [r.TargetId, r.SubjectId] : [r.SubjectId, r.TargetId];
      const next = edges.get(from);
      if (next) next.push(to);
      else edges.set(from, [to]);
    }
    index.adjacencyByPredicate.set(key, edges);
    return edges;
  }
  function thingIdsOfArchetype(archetype, index) {
    const answered = index.archetypeMembers.get(archetype);
    if (answered) return answered;
    const out = instancesBelow(typeIdsNamed(archetype, index), index);
    index.archetypeMembers.set(archetype, out);
    return out;
  }
  function typeIdsNamed(name, index) {
    const first = index.byName.get(name);
    return first ? [first.Id, ...index.otherTypesByName.get(name) ?? []] : [];
  }
  function boundBy(roleId, index) {
    const bindsId = index.predicateNameToId.get(BINDS_PREDICATE_NAME);
    if (!bindsId) return [];
    return adjacency(bindsId, false, index).get(roleId) ?? [];
  }
  function instancesBelow(typeIds, index) {
    const out = /* @__PURE__ */ new Set();
    const seen = /* @__PURE__ */ new Set();
    const frontier = [...typeIds];
    while (frontier.length) {
      const current = frontier.pop();
      if (seen.has(current)) continue;
      seen.add(current);
      for (const childId of index.isChildren.get(current) ?? []) {
        if (index.archetypeIds.has(childId)) frontier.push(childId);
        else out.add(childId);
      }
    }
    return out;
  }
  function thingsOfArchetype(archetype, index) {
    return thingsWithIds(thingIdsOfArchetype(archetype, index), index);
  }
  function thingIdBoundBy(roleId, index) {
    return boundBy(roleId, index)[0];
  }
  function thingsBoundAndBeneath(roleId, index) {
    const archetypeId = thingIdBoundBy(roleId, index);
    return archetypeId ? thingsWithIds(instancesBelow([archetypeId], index), index) : [];
  }
  function thingsWithIds(ids, index) {
    const out = [];
    for (const id of ids) {
      const t2 = index.byId.get(id);
      if (t2) out.push(t2);
    }
    return out;
  }
  function discoverDashboardsFromIndex(index, language = BASE_LANGUAGE) {
    const listed = index.dashboards.get(language);
    if (listed) return listed;
    const parsed = index.dashboards.values().next().value;
    if (parsed) {
      const inThisLanguage = [...parsed].sort((a, b) => compareNamesIn(language)(a.name, b.name));
      index.dashboards.set(language, inThisLanguage);
      return inThisLanguage;
    }
    const found = thingsBoundAndBeneath(PAGE_ROLE_ID, index).map((thing) => ({
      thing,
      specification: parseSpecification(effectiveProperties(thing, index)[DASHBOARD_SPECIFICATION_PROPERTY])
    }));
    found.sort((a, b) => compareNamesIn(language)(a.thing.Name, b.thing.Name));
    const slugs = found.map((d) => slugOf(d.thing.Name));
    const bearers = /* @__PURE__ */ new Map();
    for (const slug of slugs) bearers.set(slug, (bearers.get(slug) ?? 0) + 1);
    const out = found.map((d, i) => ({
      id: d.thing.Id,
      name: d.thing.Name,
      routeKey: slugs[i] && bearers.get(slugs[i]) === 1 ? slugs[i] : d.thing.Id,
      specification: d.specification
    }));
    index.dashboards.set(language, out);
    return out;
  }
  function slugOf(name) {
    return name.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  }
  function parseSpecification(raw) {
    let object = raw;
    if (typeof raw === "string") {
      try {
        object = JSON.parse(raw);
      } catch {
        return null;
      }
    }
    if (object && typeof object === "object" && Array.isArray(object.sections)) {
      return object;
    }
    return null;
  }
  function scopeEntities(specification, index, language = BASE_LANGUAGE) {
    if (!specification.compare) return [];
    return thingsOfArchetype(specification.compare.archetype, index).map((t2) => ({ id: t2.Id, name: t2.Name })).sort((a, b) => compareNamesIn(language)(a.name, b.name));
  }
  function namesInOrder(context) {
    return compareNamesIn(context.language ?? BASE_LANGUAGE);
  }
  function containerFor(scope, context) {
    if (!scope || !context.scopeId || scope.direction === "in") return null;
    return { within: context.scopeId, withinPredicate: scope.viaPredicate };
  }
  async function stateMemberIds(state, context) {
    return new Set((await context.reads.thingsInState(state)).Things?.map((t2) => t2.Id) ?? []);
  }
  function nullableNumber(value) {
    const asNumber2 = number(value);
    return isNaN(asNumber2) ? null : asNumber2;
  }
  function scopeMemberIds(scope, context) {
    if (!scope || !context.scopeId) return null;
    const predicateId = context.index.predicateNameToId.get(scope.viaPredicate);
    if (!predicateId) return /* @__PURE__ */ new Set();
    const edges = adjacency(predicateId, scope.direction === "in", context.index);
    const members = /* @__PURE__ */ new Set();
    const walked = /* @__PURE__ */ new Set([context.scopeId]);
    const frontier = [context.scopeId];
    while (frontier.length) {
      for (const next of edges.get(frontier.pop()) ?? []) {
        if (walked.has(next)) continue;
        walked.add(next);
        members.add(next);
        frontier.push(next);
      }
    }
    return members;
  }
  function number(v) {
    if (typeof v === "number") return v;
    if (typeof v === "boolean") return v ? 1 : 0;
    return NaN;
  }
  function passesFilters(thing, filters, index) {
    if (!filters) return true;
    const properties = effectiveProperties(thing, index);
    for (const f of filters) {
      const v = properties[f.property];
      switch (f.operator) {
        case "=":
          if (v !== f.value) return false;
          break;
        case "!=":
          if (v === f.value) return false;
          break;
        case ">":
          if (!(number(v) > number(f.value))) return false;
          break;
        case ">=":
          if (!(number(v) >= number(f.value))) return false;
          break;
        case "<":
          if (!(number(v) < number(f.value))) return false;
          break;
        case "<=":
          if (!(number(v) <= number(f.value))) return false;
          break;
        case "in":
          if (!Array.isArray(f.value) || !f.value.includes(v)) return false;
          break;
      }
    }
    return true;
  }
  function referencedThing(reference, context) {
    if (!reference || reference === SCOPE_REFERENCE) return context.scopeId ? context.index.byId.get(context.scopeId) ?? null : null;
    return context.index.byId.get(reference) ?? context.index.byName.get(reference) ?? null;
  }
  async function followStep(fromIds, step, context) {
    const predicateId = context.index.predicateNameToId.get(step.predicate);
    if (!predicateId) return [];
    const edges = adjacency(predicateId, step.direction === "in", context.index);
    const reached = /* @__PURE__ */ new Set();
    for (const id of fromIds) {
      for (const target of edges.get(id) ?? []) reached.add(target);
    }
    let ids = [...reached];
    if (step.archetype) {
      const ofArchetype = thingIdsOfArchetype(step.archetype, context.index);
      ids = ids.filter((id) => ofArchetype.has(id));
    }
    if (step.inState && ids.length) {
      const inState = await stateMemberIds(step.inState, context);
      ids = ids.filter((id) => inState.has(id));
    }
    if (step.notInState && ids.length) {
      const excluded = await stateMemberIds(step.notInState, context);
      ids = ids.filter((id) => !excluded.has(id));
    }
    return ids;
  }
  function propertyNamesByArchetype(index) {
    if (index.propertyNames) return index.propertyNames;
    const names = /* @__PURE__ */ new Map();
    index.propertyNames = names;
    const predicateId = index.predicateNameToId.get(NAMES_PROPERTY_OF_PREDICATE);
    if (!predicateId) return names;
    const namedArchetypes = adjacency(predicateId, false, index);
    for (const nameId of instancesBelow(boundBy(PROPERTY_NAME_ROLE, index), index)) {
      const name = index.byId.get(nameId);
      if (!name) continue;
      const properties = effectiveProperties(name, index);
      const property = properties[NAMED_PROPERTY];
      const words = wordingIn(properties[PROPERTY_NAME_WORDING]);
      if (typeof property !== "string" || !words) continue;
      for (const archetypeId of namedArchetypes.get(nameId) ?? []) {
        const byProperty = names.get(archetypeId) ?? /* @__PURE__ */ new Map();
        names.set(archetypeId, byProperty);
        byProperty.set(property, words);
      }
    }
    return names;
  }
  function declaredWords(property, holderIds, index) {
    const names = propertyNamesByArchetype(index);
    const seen = /* @__PURE__ */ new Set();
    let level = holderIds;
    while (level.length) {
      for (const id of level) {
        const words = names.get(id)?.get(property);
        if (words) return words;
      }
      const above = [];
      for (const id of level) {
        seen.add(id);
        for (const parentId of index.isParents.get(id) ?? []) if (!seen.has(parentId)) above.push(parentId);
      }
      level = above;
    }
    return void 0;
  }
  function leafInfluences(property, definitions, sign, visiting) {
    const definition = definitions[property];
    if (!definition) return [];
    visiting.add(property);
    const rises = new Set(definition.RisesWith ?? []);
    const falls = new Set(definition.FallsWith ?? []);
    const memberKind = definition.Expression === void 0 ? definition.RelatedType ?? void 0 : void 0;
    const leaves = [];
    for (const term of definition.Reads ?? []) {
      const direction = rises.has(term) ? 1 : falls.has(term) ? -1 : 0;
      const composed = sign * direction;
      if (composed === 0 || definition.Expression === void 0) {
        leaves.push({ term, memberKind, sign: composed });
      } else {
        const below = definitions[term] && !visiting.has(term) ? leafInfluences(term, definitions, composed, visiting) : [];
        leaves.push(...below.length ? below : [{ term, sign: composed }]);
      }
    }
    visiting.delete(property);
    return leaves;
  }
  function inputsOf(thing, index, figure) {
    const definitions = effectiveDerivedDefinitions(thing, index);
    const inputs = [];
    const leftOut = [];
    for (const derived of figure === void 0 ? Object.keys(definitions) : [figure]) {
      const routes = /* @__PURE__ */ new Map();
      for (const leaf of leafInfluences(derived, definitions, 1, /* @__PURE__ */ new Set())) {
        const key = `${leaf.term}\0${leaf.memberKind ?? ""}`;
        const seen = routes.get(key);
        if (!seen) routes.set(key, { ...leaf, ...leaf.sign === 0 ? { reason: "noDirection" } : {} });
        else if (seen.sign !== leaf.sign && !seen.reason) Object.assign(seen, { sign: 0, reason: "routesDisagree" });
      }
      for (const leaf of routes.values()) {
        const held2 = leaf.memberKind !== void 0 ? { memberKind: leaf.memberKind } : {};
        if (leaf.reason) {
          leftOut.push({ figure: derived, term: leaf.term, ...held2, reason: leaf.reason });
          continue;
        }
        const holderIds = leaf.memberKind !== void 0 ? typeIdsNamed(leaf.memberKind, index) : [thing.Id];
        const words = declaredWords(leaf.term, holderIds, index);
        inputs.push({
          figure: derived,
          term: leaf.term,
          ...leaf.memberKind !== void 0 ? held2 : { heldBy: thing.Id },
          figureMoves: leaf.sign > 0 ? "with" : "against",
          ...words ? { words } : {}
        });
      }
    }
    return { inputs, leftOut };
  }
  function leversFor(property, operator, judged, index) {
    const figureMustRise = operator === "<" || operator === "<=" ? true : operator === ">" || operator === ">=" ? false : null;
    if (figureMustRise === null) return [];
    return inputsOf(judged, index, property).inputs.map((input) => ({
      term: input.term,
      ...input.memberKind !== void 0 ? { memberArchetype: input.memberKind } : {},
      direction: input.figureMoves === "with" === figureMustRise ? "raise" : "lower",
      ...input.words ? { words: input.words } : {}
    }));
  }
  async function thingsReached(reference, via, context) {
    const start = referencedThing(reference, context);
    if (!start) return [];
    if (!via?.length) return [start];
    let reached = [start.Id];
    for (const step of via) {
      reached = await followStep(reached, step, context);
      if (!reached.length) return [];
    }
    return reached.map((id) => context.index.byId.get(id)).filter((t2) => !!t2).sort((a, b) => namesInOrder(context)(a.Name, b.Name));
  }
  async function recordedSourceOf(carrying, source, context) {
    if (!source) return { source: null, resolvedAt: null };
    const reached = await thingsReached(carrying.Id, source.via, context);
    if (!reached.length) return { source: null, resolvedAt: null };
    const named = reached.map((t2) => t2.Name).join(", ");
    if (reached.length > 1 || !source.resolvedAt) return { source: named, resolvedAt: null };
    const resolved = effectiveProperties(reached[0], context.index)[source.resolvedAt];
    return { source: named, resolvedAt: resolved == null ? null : String(resolved) };
  }
  async function withComputedColumns(rows, computed, context) {
    if (!computed?.length) return rows;
    return Promise.all(
      rows.map(async (row) => {
        const rowContext = { ...context, scopeId: row.id ?? null };
        const values = await Promise.all(computed.map((column) => resolveBinding(column.value, rowContext)));
        computed.forEach((column, i) => row[column.key] = asCell(values[i]));
        return row;
      })
    );
  }
  function withScope(body, scopeId) {
    if (body === SCOPE_REFERENCE) return scopeId;
    if (Array.isArray(body)) return body.map((item) => withScope(item, scopeId));
    if (body && typeof body === "object") {
      return Object.fromEntries(
        Object.entries(body).map(([key, value]) => [key, withScope(value, scopeId)])
      );
    }
    return body;
  }
  function selectPath(object, path) {
    if (!path) return object;
    return path.split(".").reduce((acc, key) => {
      if (acc && typeof acc === "object") return acc[key];
      return void 0;
    }, object);
  }
  function aggregateMembers(binding, context) {
    const members = scopeMemberIds(binding.scope, context);
    const ofArchetype = thingIdsOfArchetype(binding.archetype, context.index);
    const candidates = members ? [...members].filter((id) => ofArchetype.has(id)) : [...ofArchetype];
    return candidates.map((id) => context.index.byId.get(id)).filter((t2) => !!t2 && passesFilters(t2, binding.where, context.index));
  }
  function aggregateValue(binding, members, context) {
    if (binding.reduction === "count") return members.length;
    const values = members.map((t2) => number(effectiveProperties(t2, context.index)[binding.property ?? ""])).filter((n) => !isNaN(n));
    if (!values.length) return 0;
    switch (binding.reduction) {
      case "sum":
        return values.reduce((a, b) => a + b, 0);
      case "average":
        return values.reduce((a, b) => a + b, 0) / values.length;
      case "min":
        return Math.min(...values);
      case "max":
        return Math.max(...values);
    }
    return 0;
  }
  function divide(top, bottom) {
    if (top === null || bottom === null || bottom === 0) return null;
    return top / bottom;
  }
  async function resolveBinding(binding, context) {
    switch (binding.kind) {
      case "const":
        return binding.value;
      case "property":
        return resolveProperty(binding, context);
      case "aggregate":
        return aggregateValue(binding, aggregateMembers(binding, context), context);
      case "ratio": {
        const [numerator, denominator] = await Promise.all([
          resolveBinding(binding.numerator, context),
          resolveBinding(binding.denominator, context)
        ]);
        return divide(asNumber(numerator), asNumber(denominator));
      }
      case "related":
        return resolveRelated(binding, context);
      case "value":
        return resolveValue(binding, context);
      case "stateOf":
        return resolveStateOf(binding, context);
      case "verdict":
        return resolveVerdict(binding, context);
      case "origin":
        return resolveOrigin(binding, context);
      case "working":
        return resolveWorking(binding, context);
      case "compareEntities":
        return resolveCompareEntities(binding, context);
      case "stateCount":
        return resolveStateCount(binding, context);
      case "stateList":
        return resolveStateList(binding, context);
      case "thingList":
        return resolveThingList(binding, context);
      case "timeseries":
        return resolveTimeseries(binding, context);
      case "latest": {
        const points = await seriesPoints(binding.series, context);
        return points?.length ? points[points.length - 1] : null;
      }
      case "service":
        return resolveService(binding, context);
      case "history":
        return resolveHistory(binding, context);
    }
  }
  function resolveProperty(binding, context) {
    if (binding.thing === SCOPE_REFERENCE) {
      if (context.scopeId) {
        const t3 = context.index.byId.get(context.scopeId);
        return t3 ? number(effectiveProperties(t3, context.index)[binding.property]) : null;
      }
      const entities = context.compareArchetype ? thingsOfArchetype(context.compareArchetype, context.index) : [];
      const values = entities.map((t3) => number(effectiveProperties(t3, context.index)[binding.property])).filter((n) => !isNaN(n));
      return values.length ? values.reduce((a, b) => a + b, 0) / values.length : null;
    }
    const t2 = referencedThing(binding.thing, context);
    return t2 ? number(effectiveProperties(t2, context.index)[binding.property]) : null;
  }
  async function resolveRelated(binding, context) {
    const reached = await thingsReached(binding.thing, binding.via, context);
    if (!reached.length) return null;
    const values = reached.map((t2) => binding.property ? effectiveProperties(t2, context.index)[binding.property] : t2.Name).filter((v) => v != null && v !== "");
    if (!values.length) return null;
    if (values.length === 1 && typeof values[0] === "number") return values[0];
    return [...new Set(values.map(String))].sort(namesInOrder(context)).join(", ");
  }
  async function resolveValue(binding, context) {
    const reached = await thingsReached(binding.thing, binding.via, context);
    if (reached.length !== 1) return null;
    return heldAsIs(effectiveProperties(reached[0], context.index)[binding.property]);
  }
  function heldAsIs(value) {
    if (typeof value === "number" || typeof value === "string") return value;
    if (value === null || typeof value !== "object" || Array.isArray(value)) return null;
    const { Json } = value;
    if (typeof Json !== "string") return value;
    try {
      const geometry = JSON.parse(Json);
      return geometry !== null && typeof geometry === "object" && !Array.isArray(geometry) ? geometry : null;
    } catch {
      return null;
    }
  }
  async function resolveStateOf(binding, context) {
    const t2 = referencedThing(binding.thing, context);
    if (!t2) return null;
    for (const state of binding.states) {
      if ((await stateMemberIds(state, context)).has(t2.Id)) return state;
    }
    return null;
  }
  async function resolveVerdict(binding, context) {
    const judged = await thingsReached(binding.thing, binding.via, context);
    if (!judged.length) return [];
    const membership = await Promise.all(binding.states.map((c) => stateMemberIds(c.state, context)));
    const verdictsOf = await Promise.all(judged.map(async (thing) => {
      const held2 = binding.states.filter((_, i) => membership[i].has(thing.Id));
      if (!held2.length) return [];
      const ranges = await context.reads.thingRanges(thing.Id);
      const properties = effectiveProperties(thing, context.index);
      return held2.map((candidate) => {
        const judgedAgainst = ranges ? findRange(candidate.state, ranges)?.Comparisons?.[0] : void 0;
        const levers = candidate.levers && judgedAgainst ? leversFor(judgedAgainst.PropertyName, judgedAgainst.Operator, thing, context.index) : [];
        return {
          state: candidate.state,
          reads: candidate.reads,
          property: judgedAgainst ? judgedAgainst.PropertyName : null,
          operator: judgedAgainst ? judgedAgainst.Operator : null,
          target: judgedAgainst ? nullableNumber(judgedAgainst.Value) : null,
          value: judgedAgainst ? nullableNumber(properties[judgedAgainst.PropertyName]) : null,
          ...levers.length ? { levers } : {}
        };
      });
    }));
    return verdictsOf.flat();
  }
  async function resolveOrigin(binding, context) {
    const carrying = await thingsReached(binding.thing, binding.via, context);
    return Promise.all(carrying.map(async (thing) => {
      const { origin, assumedFrom } = valueOrigin(thing, binding.property, context.index);
      const recorded = assumedFrom !== null ? { source: assumedFrom, resolvedAt: null } : await recordedSourceOf(thing, binding.source, context);
      return { origin, reads: binding.reads[origin] ?? null, ...recorded };
    }));
  }
  async function resolveWorking(binding, context) {
    const computing = await thingsReached(binding.thing, binding.via, context);
    return computing.flatMap((thing) => {
      const definition = effectiveDerivedDefinitions(thing, context.index)[binding.property];
      if (!definition) return [];
      const terms = definition.Reads ?? [];
      const formula = definition.Expression;
      if (formula === void 0) {
        return terms.map((term) => ({
          formula: null,
          term,
          memberArchetype: definition.RelatedType ?? null
        }));
      }
      const properties = effectiveProperties(thing, context.index);
      return terms.map((term) => ({
        formula,
        term,
        value: nullableNumber(properties[term])
      }));
    });
  }
  function resolveCompareEntities(binding, context) {
    const entities = context.compareArchetype ? thingsOfArchetype(context.compareArchetype, context.index) : [];
    entities.sort((a, b) => namesInOrder(context)(a.Name, b.Name));
    const rows = entities.map((t2) => {
      const row = { id: t2.Id, name: t2.Name };
      for (const p of binding.properties) row[p] = number(effectiveProperties(t2, context.index)[p]);
      return row;
    });
    return withComputedColumns(rows, binding.computed, context);
  }
  async function resolveStateCount(binding, context) {
    const container = containerFor(binding.scope, context);
    const narrowing = {
      type: binding.archetype,
      notIn: binding.excludeState ? [binding.excludeState] : void 0,
      ...container
    };
    const members = container ? null : scopeMemberIds(binding.scope, context);
    if (members) {
      const response2 = await context.reads.thingsInState(binding.state, narrowing);
      return (response2.Things ?? []).filter((t2) => members.has(t2.Id)).length;
    }
    const response = await context.reads.thingsInState(binding.state, { ...narrowing, countOnly: true });
    if (response.Count === void 0) throw new Error(tendrilWord("stateCountMissing", { state: binding.state }));
    return response.Count;
  }
  async function resolveStateList(binding, context) {
    const container = containerFor(binding.scope, context);
    const members = container ? null : scopeMemberIds(binding.scope, context);
    const response = await context.reads.thingsInState(binding.state, {
      type: binding.archetype,
      // A Thing in a later stage still holds every earlier stage's state, so excluding the next stage's state keeps only the Things that reached this stage and no further.
      notIn: binding.excludeState ? [binding.excludeState] : void 0,
      // A cap the server applied would keep its first rows before the scope is narrowed here, so the cap is applied after narrowing.
      limit: members ? void 0 : binding.limit,
      properties: binding.properties,
      ...container
    });
    let list = response.Things ?? [];
    if (members) {
      list = list.filter((t2) => members.has(t2.Id));
      if (binding.limit) list = list.slice(0, binding.limit);
    }
    const rows = list.map((reference) => ({ id: reference.Id, name: reference.Name, ...reference.Properties ?? {} }));
    return withComputedColumns(rows, binding.computed, context);
  }
  async function resolveThingList(binding, context) {
    let list = thingsOfArchetype(binding.archetype, context.index);
    const members = scopeMemberIds(binding.scope, context);
    if (members) list = list.filter((t2) => members.has(t2.Id));
    if (binding.where) list = list.filter((t2) => passesFilters(t2, binding.where, context.index));
    if (binding.inState && list.length) {
      const inState = new Set(((await context.reads.thingsInState(binding.inState, { type: binding.archetype })).Things ?? []).map((t2) => t2.Id));
      list = list.filter((t2) => inState.has(t2.Id));
    }
    list.sort((a, b) => namesInOrder(context)(a.Name, b.Name));
    if (binding.limit) list = list.slice(0, binding.limit);
    const rows = list.map((t2) => ({ id: t2.Id, name: t2.Name, ...effectiveProperties(t2, context.index) }));
    return withComputedColumns(rows, binding.computed, context);
  }
  async function resolveService(binding, context) {
    try {
      const response = await context.reads.fromService(binding.endpoint, withScope(binding.body ?? {}, context.scopeId));
      const picked = selectPath(response, binding.select);
      return picked ?? null;
    } catch {
      return null;
    }
  }
  var REDUCTIONS = {
    count: "Count",
    sum: "Sum",
    average: "Average",
    min: "Min",
    max: "Max"
  };
  var FOLDS = {
    count: (buckets) => buckets.reduce((total, bucket) => total + bucket, 0),
    sum: (buckets) => buckets.reduce((total, bucket) => total + bucket, 0),
    min: (buckets) => Math.min(...buckets),
    max: (buckets) => Math.max(...buckets)
  };
  async function seriesPoints(binding, context) {
    if (binding.scope?.direction === "in") {
      context.warn?.(
        `timeseries over ${binding.archetype}: the platform narrows to what a container reaches, so a scope reaching the other way cannot be asked for.`
      );
      return null;
    }
    const bucketsPerPoint = binding.bucketsPerPoint ?? 1;
    const fold = bucketsPerPoint === 1 ? (buckets) => buckets[0] : FOLDS[binding.reduction];
    if (!fold) {
      context.warn?.(
        `timeseries over ${binding.archetype}: an ${binding.reduction} of several buckets is not the ${binding.reduction} of their values, so a point cannot be folded up from them.`
      );
      return null;
    }
    try {
      const answer = await context.reads.aggregate({
        function: REDUCTIONS[binding.reduction],
        memberType: binding.archetype,
        timestampProperty: binding.happenedAt,
        // A count reduces the members themselves, so naming a measure would ask for a value the
        // question is not about.
        measureProperty: binding.reduction === "count" ? void 0 : binding.property,
        // The oldest point covers buckets that start before it does, so the window runs back further
        // than the points do.
        windowSeconds: binding.bucketSeconds * (binding.buckets + bucketsPerPoint - 1),
        bucketSeconds: binding.bucketSeconds,
        ...containerFor(binding.scope, context)
      });
      const buckets = answer?.Buckets ?? [];
      const points = [];
      for (let oldest = 0; oldest + bucketsPerPoint <= buckets.length; oldest++)
        points.push(fold(buckets.slice(oldest, oldest + bucketsPerPoint)));
      return points;
    } catch {
      return null;
    }
  }
  async function resolveTimeseries(binding, context) {
    const points = await seriesPoints(binding, context);
    if (points === null) return null;
    if (binding.buckets > 1) return points;
    return points.length ? points[0] : null;
  }
  async function resolveHistory(binding, context) {
    if (!context.scopeId) return null;
    const entity = context.index.byId.get(context.scopeId);
    const offset = entity ? nullableNumber(effectiveProperties(entity, context.index)[UTC_OFFSET_PROPERTY]) : null;
    const steps = await resolveSteps(binding.steps, context);
    if (!steps) return null;
    try {
      const answer = await context.reads.reduce({
        thingId: context.scopeId,
        property: binding.property,
        windowSeconds: binding.windowSeconds,
        ...offset !== null ? { utcOffsetSeconds: offset } : {},
        steps,
        ...binding.bins ? { bins: binding.bins } : {},
        ...binding.pairedWith ? { pairedWith: binding.pairedWith } : {}
      });
      if (binding.answer === "coverage")
        return [{ samples: answer.Samples, unpairedSamples: answer.UnpairedSamples ?? 0, unusableSamples: answer.UnusableSamples }];
      return answer.Groups.map((group) => ({ key: group.Key, value: group.Value }));
    } catch {
      return null;
    }
  }
  var STEP_PARAMETERS = ["percentile", "from", "to", "threshold"];
  async function resolveSteps(steps, context) {
    const resolved = [];
    for (const step of steps) {
      const numeric = { fold: step.fold, function: step.function };
      for (const name of STEP_PARAMETERS) {
        const given = step[name];
        if (given === void 0) continue;
        const value = typeof given === "number" ? given : asNumber(await resolveBinding(given, context));
        if (value === null) return null;
        numeric[name] = value;
      }
      resolved.push(numeric);
    }
    return resolved;
  }
  function asNumber(r) {
    return typeof r === "number" && !isNaN(r) ? r : null;
  }
  function asCell(r) {
    if (typeof r === "string") return r;
    return asNumber(r);
  }

  // vos.Tendril/src/api/pageSelector.ts
  var NAVIGATION_AND_SETTINGS = {
    ids: [PAGE_ROLE_ID, DISPLAY_SETTINGS_ROLE_ID, PREDICATE_ROLE_ID],
    boundTypes: [PAGE_ROLE_ID],
    boundArchetypes: [DISPLAY_SETTINGS_ROLE_ID],
    names: [IS_PREDICATE, BINDS_PREDICATE_NAME],
    includeLaterMatches: true
  };
  var UNBOUNDED_DEPTH = 2147483647;
  var IDENTIFIER = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  function ruleFor(predicate, direction, depth) {
    const followed = direction === "in" ? "incoming" : "outgoing";
    return { predicate, direction: followed, depth };
  }
  function widgetBindings(widget) {
    switch (widget.type) {
      case "kpi":
        return [widget.value, widget.delta, widget.spark, widget.sparkBaseline, widget.origin];
      case "funnel":
        return widget.stages.flatMap((stage) => [stage.count, stage.drill]);
      case "bullet":
        return widget.rows.map((row) => row.value);
      case "table":
        return [widget.rows];
      case "gantt":
        return [widget.rows];
      case "leaderboard":
        return [widget.entities];
      case "verdict":
        return widget.rows.map((row) => row.verdicts);
      case "working":
        return widget.rows.flatMap((row) => [row.value, row.working]);
      case "exceptionBar":
        return widget.buckets.map((bucket) => bucket.value);
      case "rangeBar":
        return [
          ...[widget.months, widget.annual].flatMap((period) => period ? [
            period.recordedHigh,
            period.designHigh,
            period.averageHigh,
            period.mean,
            period.averageLow,
            period.designLow,
            period.recordedLow
          ] : []),
          ...(widget.bands ?? []).flatMap((band) => [band.from, band.to])
        ];
      case "boxPlot":
        return [
          ...[widget.months, widget.annual].flatMap((period) => period ? [period.whiskerLow, period.boxLow, period.median, period.boxHigh, period.whiskerHigh] : []),
          ...(widget.bands ?? []).flatMap((band) => [band.from, band.to])
        ];
      case "depthProfile":
        return [widget.rows];
      case "polarShares":
        return [widget.counts, widget.cells];
      case "densityGrid":
        return [widget.cells, widget.coverage, ...(widget.outlines ?? []).map((outline) => outline.shape)];
      case "gridMap":
        return [widget.grid, widget.outline, ...(widget.classes ?? []).flatMap((entry) => [entry.from, entry.to])];
      case "lineSeries":
        return widget.series.map((entry) => entry.value);
      case "heatmap":
        return [
          widget.value,
          widget.sun?.latitude,
          widget.sun?.longitude,
          widget.sun?.utcOffsetSeconds,
          ...(widget.classes ?? []).flatMap((entry) => [entry.from, entry.to])
        ];
      case "stackedShares":
        return [
          ...widget.classes.flatMap((entry) => [entry.share, typeof entry.colour === "object" ? entry.colour : void 0, entry.annual]),
          widget.counted?.counts,
          widget.counted?.annual,
          widget.counted?.groups
        ];
      case "divergingBar":
        return [widget.up.value, widget.up.threshold, widget.down.value, widget.down.threshold];
      case "smallMultiples":
        return [widget.bars.value, widget.line.value, widget.band?.from, widget.band?.to];
      case "action":
        return [
          widget.rows,
          widget.writes.choicesFrom?.from,
          widget.decided?.rows,
          ...[...widget.asks ?? [], ...widget.writes.choices.flatMap((choice) => choice.asks ?? [])].map((field) => field.options)
        ];
      case "form":
        return widget.fields.map((field) => field.options);
    }
  }
  function withNested(binding) {
    const inner = [];
    if (binding.kind === "ratio") inner.push(binding.numerator, binding.denominator);
    if (binding.kind === "latest") inner.push(binding.series);
    if (binding.kind === "history") {
      for (const step of binding.steps)
        for (const parameter of [step.percentile, step.from, step.to, step.threshold])
          if (typeof parameter === "object") inner.push(parameter);
    }
    if ("computed" in binding) {
      for (const column of binding.computed ?? []) inner.push(column?.value);
    }
    return [binding, ...inner.filter((nested) => !!nested).flatMap(withNested)];
  }
  function slotsOf(widget) {
    return (widgetBindings(widget) ?? []).filter((binding) => !!binding);
  }
  function bindingsOf(widget) {
    return slotsOf(widget).flatMap(withNested);
  }
  function specificationBindings(specification) {
    return specification.sections.flatMap((section) => section.widgets).flatMap(bindingsOf);
  }
  function detailWalks(relations) {
    if (!relations?.length) return [];
    return relations.flatMap((relation) => {
      const step = { predicate: relation.predicate, direction: relation.direction };
      const below = detailWalks(relation.relations);
      return below.length ? below.map((path) => [step, ...path]) : [[step]];
    });
  }
  function bindingWalk(binding) {
    if (binding.kind === "origin") return [...binding.via ?? [], ...binding.source?.via ?? []];
    return "via" in binding ? binding.via : void 0;
  }
  function specificationWalks(specification) {
    const fromBindings = specificationBindings(specification).map(bindingWalk).filter((via) => !!via?.length);
    return [...fromBindings, ...detailWalks(specification.detail?.relations)];
  }
  function scopeRules(specification) {
    const seen = /* @__PURE__ */ new Set();
    const rules = [];
    for (const binding of specificationBindings(specification)) {
      const scope = "scope" in binding ? binding.scope : void 0;
      if (!scope) continue;
      const key = `${scope.viaPredicate}:${scope.direction ?? "out"}`;
      if (seen.has(key)) continue;
      seen.add(key);
      rules.push(ruleFor(scope.viaPredicate, scope.direction, UNBOUNDED_DEPTH));
    }
    return rules;
  }
  function walkRules(walks) {
    const deepest = walks.reduce((longest, walk) => Math.max(longest, walk.length), 0);
    const rules = [];
    for (let position = 0; position < deepest; position++) {
      const seen = /* @__PURE__ */ new Set();
      for (const walk of walks) {
        const step = walk[position];
        if (!step) continue;
        const key = `${step.predicate}:${step.direction ?? "out"}`;
        if (seen.has(key)) continue;
        seen.add(key);
        rules.push(ruleFor(step.predicate, step.direction, 1));
      }
    }
    return rules;
  }
  var READS_ITS_TYPE_LOCALLY = /* @__PURE__ */ new Set(["thingList", "aggregate", "stateList"]);
  function drawnTypes(specification, scopeId) {
    const types = /* @__PURE__ */ new Set();
    if (specification.compare) types.add(specification.compare.archetype);
    for (const binding of specificationBindings(specification)) {
      if (!READS_ITS_TYPE_LOCALLY.has(binding.kind)) continue;
      const archetype = "archetype" in binding ? binding.archetype : void 0;
      if (!archetype) continue;
      const narrowed = "scope" in binding && !!binding.scope;
      if (!narrowed || scopeId === null) types.add(archetype);
    }
    return [...types];
  }
  function namedThings(specification) {
    const referenced = /* @__PURE__ */ new Set();
    for (const binding of specificationBindings(specification)) {
      const thing = "thing" in binding ? binding.thing : void 0;
      if (thing && thing !== SCOPE_REFERENCE) referenced.add(thing);
    }
    return {
      ids: [...referenced].filter((reference) => IDENTIFIER.test(reference)),
      names: [...referenced].filter((reference) => !IDENTIFIER.test(reference))
    };
  }
  function subscriptionForSpecification(specification, scopeId, alsoRead = []) {
    const named = namedThings(specification);
    const levers = offersLevers(specification);
    const traverse = [
      ...scopeRules(specification),
      ...walkRules(specificationWalks(specification)),
      ...levers ? [ruleFor(NAMES_PROPERTY_OF_PREDICATE, "out", 1)] : [],
      ruleFor(BINDS_PREDICATE_NAME, "out", 1)
    ];
    const selector = {
      ids: [.../* @__PURE__ */ new Set([
        ...NAVIGATION_AND_SETTINGS.ids,
        ...scopeId ? [scopeId] : [],
        ...named.ids,
        ...levers ? [PROPERTY_NAME_ROLE] : [],
        ...alsoRead
      ])],
      boundTypes: [...NAVIGATION_AND_SETTINGS.boundTypes, ...levers ? [PROPERTY_NAME_ROLE] : []],
      boundArchetypes: NAVIGATION_AND_SETTINGS.boundArchetypes,
      types: drawnTypes(specification, scopeId),
      names: [.../* @__PURE__ */ new Set([
        ...NAVIGATION_AND_SETTINGS.names,
        ...traverse.map((rule) => rule.predicate),
        ...named.names,
        ...predicatesAPressRelatesAlong(specification)
      ])],
      includeLaterMatches: true,
      includeObservations: true
    };
    if (traverse.length) selector.traverse = traverse;
    return selector;
  }
  function predicatesAPressRelatesAlong(specification) {
    return specification.sections.flatMap((section) => section.widgets).flatMap((widget) => widget.type === "action" && widget.writes.choicesFrom ? [widget.writes.choicesFrom.predicate] : []);
  }
  function offersLevers(specification) {
    return specificationBindings(specification).some((binding) => binding.kind === "verdict" && binding.states.some((state) => !!state.levers));
  }

  // vos.Pages/bundle/readerHost.ts
  void instance.init({
    lng: BASE_LANGUAGE,
    initAsync: false,
    resources: Object.fromEntries(Object.entries(TENDRIL_WORDS).map(([language, words]) => [language, { [TENDRIL_NAMESPACE]: words }]))
  });
  var held = /* @__PURE__ */ new Map();
  var toldOfAWarning;
  function indexHeldAs(key) {
    const index = held.get(key);
    if (!index) throw new Error(`No Things are held as "${key}".`);
    return index;
  }
  function pageOf(index, pageId) {
    const page = discoverDashboardsFromIndex(index).find((candidate) => candidate.id === pageId);
    if (!page) throw new Error(`The Things held include no page with the identifier ${pageId}.`);
    return page;
  }
  function readsAnsweredBy(host) {
    const ask = async (method, ...given) => JSON.parse(await host.read(method, JSON.stringify(given)));
    return {
      thingsInState: (state, narrowing) => ask("thingsInState", state, narrowing ?? null),
      thingRanges: (thingId) => ask("thingRanges", thingId),
      aggregate: (query) => ask("aggregate", query),
      reduce: (query) => ask("reduce", query),
      fromService: (endpoint, body) => ask("fromService", endpoint, body ?? null)
    };
  }
  var pageReader = {
    tellWarningsTo(told) {
      toldOfAWarning = told;
    },
    navigationSelector() {
      return JSON.stringify(NAVIGATION_AND_SETTINGS);
    },
    hold(key, thingsJson, relationshipsJson) {
      held.set(key, buildModelIndex(JSON.parse(thingsJson).map(unwrapThing), JSON.parse(relationshipsJson).map(unwrapRelationship)));
    },
    release(key) {
      held.delete(key);
    },
    listPages(key, language) {
      return JSON.stringify(discoverDashboardsFromIndex(indexHeldAs(key), language).map((page) => ({
        id: page.id,
        name: page.name,
        address: page.routeKey,
        readable: page.specification !== null
      })));
    },
    /** What one page needs sent, to be read for no scope and for each of the given ones. Nothing is
     *  asked to be kept up to date: the Things are read once and let go. */
    selectorFor(key, pageId, scopeIdsJson) {
      const page = pageOf(indexHeldAs(key), pageId);
      if (!page.specification) throw new Error(`The description of the page "${page.name}" could not be read.`);
      const selector = subscriptionForSpecification(page.specification, null);
      const scopeIds = JSON.parse(scopeIdsJson);
      return JSON.stringify({ ...selector, ids: [.../* @__PURE__ */ new Set([...selector.ids ?? [], ...scopeIds])], includeLaterMatches: false, includeObservations: false });
    },
    scopesOf(key, pageId, language) {
      const index = indexHeldAs(key);
      const page = pageOf(index, pageId);
      return JSON.stringify(page.specification ? scopeEntities(page.specification, index, language) : []);
    },
    async readPage(key, pageId, scopeId, language, host) {
      const index = indexHeldAs(key);
      const page = pageOf(index, pageId);
      if (!page.specification) throw new Error(`The description of the page "${page.name}" could not be read.`);
      const context = {
        index,
        scopeId,
        compareArchetype: page.specification.compare?.archetype,
        reads: readsAnsweredBy(host),
        language,
        warn: toldOfAWarning
      };
      const sections = [];
      for (const section of page.specification.sections) {
        const widgets = [];
        for (const widget of section.widgets) {
          const bindings = [];
          for (const binding of slotsOf(widget)) {
            try {
              bindings.push({ kind: binding.kind, result: await resolveBinding(binding, context) });
            } catch (refused) {
              bindings.push({ kind: binding.kind, unreadable: refused instanceof Error ? refused.message : String(refused) });
            }
          }
          widgets.push({ type: widget.type, title: widget.title ?? null, bindings });
        }
        sections.push({ title: section.title ?? null, widgets });
      }
      return JSON.stringify({ page: page.name, scopeId, language, sections });
    }
  };
  globalThis.pageReader = pageReader;
})();
