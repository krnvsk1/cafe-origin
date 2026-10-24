/**
 * Подпись КЭП в браузере через «КриптоПро ЭЦП Browser plug-in».
 * Нужны: КриптоПро CSP, плагин и расширение для браузера, сертификат на токене (Рутокен/JaCarta).
 * Ключ не покидает компьютер пользователя — на сервер уходит только готовая подпись.
 */

const PLUGIN_SCRIPT = 'https://www.cryptopro.ru/sites/default/files/products/cades/cadesplugin_api.js';

// Константы CAPICOM/CAdESCOM (на случай, если плагин их не выставил)
const CAPICOM_CURRENT_USER_STORE = 2;
const CAPICOM_MY_STORE = 'My';
const CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED = 2;
const CAPICOM_CERTIFICATE_FIND_SHA1_HASH = 0;
const CADESCOM_BASE64_TO_BINARY = 1;
const CADESCOM_CADES_BES = 1;

/* eslint-disable @typescript-eslint/no-explicit-any */
type Plugin = any;

declare global {
  interface Window {
    cadesplugin?: Plugin;
  }
}

export interface Certificate {
  thumbprint: string;
  subject: string;
  /** Владелец (CN) */
  name: string;
  /** Организация (O) */
  organization: string;
  inn: string | null;
  validFrom: Date;
  validTo: Date;
}

let loading: Promise<Plugin> | null = null;

/** Загрузить и дождаться плагин; понятная ошибка, если он не установлен */
export const loadCryptoPro = (): Promise<Plugin> => {
  if (loading) return loading;
  loading = new Promise<Plugin>((resolve, reject) => {
    const ready = () => {
      const plugin = window.cadesplugin;
      if (!plugin) {
        reject(new Error('Плагин КриптоПро не найден'));
        return;
      }
      // В современных браузерах cadesplugin — промис, который выполняется, когда расширение готово
      Promise.race([
        Promise.resolve(plugin).then(() => plugin),
        new Promise((_, rej) => setTimeout(() => rej(new Error('timeout')), 15000)),
      ])
        .then(() => resolve(plugin))
        .catch(() => reject(new Error(
          'Не удалось подключиться к плагину КриптоПро. Проверьте, что установлены КриптоПро CSP, ' +
          '«КриптоПро ЭЦП Browser plug-in» и расширение для браузера, и разрешите сайту доступ к плагину.'
        )));
    };
    if (window.cadesplugin) {
      ready();
      return;
    }
    const script = document.createElement('script');
    script.src = PLUGIN_SCRIPT;
    script.async = true;
    script.onload = ready;
    script.onerror = () => reject(new Error('Не удалось загрузить скрипт плагина КриптоПро — проверьте интернет'));
    document.head.appendChild(script);
  }).catch(e => {
    loading = null;
    throw e;
  });
  return loading;
};

const field = (subject: string, ...keys: string[]) => {
  for (const key of keys) {
    const m = subject.match(new RegExp(`(?:^|,\\s*)${key.replace(/\./g, '\\.')}=("?)([^,"]+)\\1`));
    if (m) return m[2].trim();
  }
  return '';
};

const openStore = async (plugin: Plugin) => {
  const store = await plugin.CreateObjectAsync('CAdESCOM.Store');
  await store.Open(
    plugin.CAPICOM_CURRENT_USER_STORE ?? CAPICOM_CURRENT_USER_STORE,
    plugin.CAPICOM_MY_STORE ?? CAPICOM_MY_STORE,
    plugin.CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED ?? CAPICOM_STORE_OPEN_MAXIMUM_ALLOWED,
  );
  return store;
};

/** Сертификаты пользователя с закрытым ключом */
export const listCertificates = async (): Promise<Certificate[]> => {
  const plugin = await loadCryptoPro();
  const store = await openStore(plugin);
  try {
    const certs = await store.Certificates;
    const count: number = await certs.Count;
    const result: Certificate[] = [];
    for (let i = 1; i <= count; i++) {
      const cert = await certs.Item(i);
      let hasKey = true;
      try { hasKey = await cert.HasPrivateKey(); } catch { /* старые версии плагина */ }
      if (!hasKey) continue;
      const subject: string = await cert.SubjectName;
      result.push({
        thumbprint: await cert.Thumbprint,
        subject,
        name: field(subject, 'CN'),
        organization: field(subject, 'O'),
        inn: field(subject, 'ИНН ЮЛ', 'INNLE', '1.2.643.100.4', 'ИНН', 'INN', '1.2.643.3.131.1.1') || null,
        validFrom: new Date(await cert.ValidFromDate),
        validTo: new Date(await cert.ValidToDate),
      });
    }
    return result.sort((a, b) => b.validTo.getTime() - a.validTo.getTime());
  } finally {
    try { await store.Close(); } catch { /* ignore */ }
  }
};

const findCertificate = async (plugin: Plugin, thumbprint: string) => {
  const store = await openStore(plugin);
  try {
    const certs = await store.Certificates;
    const found = await certs.Find(plugin.CAPICOM_CERTIFICATE_FIND_SHA1_HASH ?? CAPICOM_CERTIFICATE_FIND_SHA1_HASH, thumbprint);
    if ((await found.Count) === 0) throw new Error('Сертификат не найден — вставьте токен с ключом');
    return await found.Item(1);
  } finally {
    try { await store.Close(); } catch { /* ignore */ }
  }
};

const utf8ToBase64 = (text: string) => {
  const bytes = new TextEncoder().encode(text);
  let binary = '';
  bytes.forEach(b => { binary += String.fromCharCode(b); });
  return btoa(binary);
};

/**
 * Подпись CAdES-BES. detached = true — откреплённая (для документов), false — присоединённая (для входа).
 * content — данные в base64.
 */
export const signBase64 = async (thumbprint: string, contentBase64: string, detached: boolean): Promise<string> => {
  const plugin = await loadCryptoPro();
  const cert = await findCertificate(plugin, thumbprint);
  const signer = await plugin.CreateObjectAsync('CAdESCOM.CPSigner');
  await signer.propset_Certificate(cert);
  const data = await plugin.CreateObjectAsync('CAdESCOM.CadesSignedData');
  await data.propset_ContentEncoding(plugin.CADESCOM_BASE64_TO_BINARY ?? CADESCOM_BASE64_TO_BINARY);
  await data.propset_Content(contentBase64);
  try {
    const signature: string = await data.SignCades(signer, plugin.CADESCOM_CADES_BES ?? CADESCOM_CADES_BES, detached);
    return signature.replace(/\s+/g, '');
  } catch (e) {
    const message = (e as { message?: string })?.message ?? String(e);
    throw new Error(message.includes('отменена') || message.includes('cancel')
      ? 'Подпись отменена'
      : `Не удалось подписать: ${message}`);
  }
};

/** Подписать строку (присоединённая подпись) — для входа в Честный знак */
export const signText = (thumbprint: string, text: string) => signBase64(thumbprint, utf8ToBase64(text), false);
