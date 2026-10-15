#!/usr/bin/env python3
"""
user_info.py - Extract full user lifecycle and journey for OpenRussiaRouter.
Combines PostgreSQL data (Auth, Billing, Analytics) with Yandex Metrika.

Zero external python dependencies (standard library only).

Usage:
  # 1. Via SSH key (if configured on the server):
  python3 user_info.py --email user@example.com

  # 2. Via SSH password:
  SSH_PASSWORD="xxx" python3 user_info.py --email user@example.com

  # 3. Supplying Yandex token explicitly or via env:
  YANDEX_OAUTH_TOKEN="xxx" python3 user_info.py --email user@example.com
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone

DEFAULT_SSH_HOST = "root@89.191.225.228"
DEFAULT_METRIKA_COUNTER = "111582266"


def resolve_yandex_token(explicit_token=None, token_file=None):
    """Resolves Yandex OAuth token from env, argument or common candidate file paths."""
    if explicit_token:
        return explicit_token.strip()

    # 1. Check environment variables
    for env_var in ("YANDEX_OAUTH_TOKEN", "YANDEX_METRIKA_TOKEN", "YD_OAUTH_TOKEN"):
        val = os.environ.get(env_var)
        if val:
            return val.strip()

    # 2. Candidate file locations (relative and home-based)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        token_file,
        os.path.abspath(os.path.join(script_dir, "../../../yandex_direct/.pi/secrets/yandex-ads-token")),
        os.path.abspath(os.path.join(script_dir, "../../../../yandex_direct/.pi/secrets/yandex-ads-token")),
        os.path.expanduser("~/.config/orr/yandex-ads-token"),
        os.path.expanduser("~/Documents/projects/github/openrussiarouter/yandex_direct/.pi/secrets/yandex-ads-token"),
    ]
    for path in candidates:
        if path and os.path.isfile(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    token = f.read().strip()
                    if token:
                        return token
            except Exception:
                pass

    return None


def is_ssh_key_working(ssh_host):
    """Checks if passwordless SSH key authentication works."""
    cmd = [
        "ssh",
        "-o", "BatchMode=yes",
        "-o", "ConnectTimeout=3",
        "-o", "StrictHostKeyChecking=no",
        "-o", "UserKnownHostsFile=/dev/null",
        ssh_host,
        "echo OK",
    ]
    try:
        res = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=5)
        return res.returncode == 0 and "OK" in res.stdout
    except Exception:
        return False


def run_remote_sql(ssh_host, ssh_password, db_name, sql, use_key=False):
    """Executes SQL inside the docker container on the remote host."""
    docker_cmd = f"docker exec -i orr-postgres-1 psql -U postgres -d {db_name} -t -A -F \x1f -c \"{sql}\""

    if use_key:
        cmd = [
            "ssh",
            "-o", "StrictHostKeyChecking=no",
            "-o", "UserKnownHostsFile=/dev/null",
            ssh_host,
            docker_cmd,
        ]
        proc = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        stdout = proc.stdout
    else:
        # Use expect with password in memory
        expect_script = f"""
set timeout 30
set password $env(SSHPASS)
spawn ssh -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null {ssh_host} {{{docker_cmd}}}
expect {{
    "*password:*" {{
        send "$password\\r"
        exp_continue
    }}
}}
"""
        env = os.environ.copy()
        env["SSHPASS"] = ssh_password or ""
        proc = subprocess.Popen(
            ["expect", "-c", expect_script],
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        stdout, _ = proc.communicate()

    lines = []
    for line in stdout.splitlines():
        clean = line.strip()
        if not clean:
            continue
        if clean.startswith("spawn ") or "Permanently added" in clean or "password:" in clean:
            continue
        lines.append(clean)
    return lines


def fetch_db_data(ssh_host, ssh_password, email, use_key=False):
    data = {}

    # 1. User
    sql_user = f"SELECT id, email, role, status, created_at_utc FROM auth.users WHERE lower(email) = lower('{email}');"
    rows = run_remote_sql(ssh_host, ssh_password, "openrussiarouter", sql_user, use_key)
    if not rows:
        return None
    parts = rows[0].split("\x1f")
    user_id = parts[0]
    data["user"] = {
        "id": user_id,
        "email": parts[1],
        "role": parts[2],
        "status": parts[3],
        "created_at_utc": parts[4],
    }

    # 2. API Keys
    sql_keys = f"SELECT id, name, prefix, created_at_utc, last_used_at_utc, disabled_at_utc, deleted_at_utc FROM auth.api_keys WHERE user_id = '{user_id}' ORDER BY created_at_utc ASC;"
    key_rows = run_remote_sql(ssh_host, ssh_password, "openrussiarouter", sql_keys, use_key)
    data["api_keys"] = []
    for r in key_rows:
        kp = r.split("\x1f")
        if len(kp) >= 7:
            data["api_keys"].append({
                "id": kp[0],
                "name": kp[1],
                "prefix": kp[2],
                "created_at_utc": kp[3],
                "last_used_at_utc": kp[4],
                "disabled_at_utc": kp[5],
                "deleted_at_utc": kp[6],
            })

    # 3. Balance
    sql_bal = f"SELECT balance_amount, updated_at_utc FROM billing.user_balances WHERE user_id = '{user_id}';"
    bal_rows = run_remote_sql(ssh_host, ssh_password, "openrussiarouter", sql_bal, use_key)
    if bal_rows:
        bp = bal_rows[0].split("\x1f")
        data["balance"] = {"amount": bp[0], "updated_at_utc": bp[1]}

    # 4. Payments
    sql_pay = f"SELECT id, provider, display_amount, display_currency, credited_usd_amount, status, created_at_utc, confirmed_at_utc FROM billing.payments WHERE user_id = '{user_id}' ORDER BY created_at_utc ASC;"
    pay_rows = run_remote_sql(ssh_host, ssh_password, "openrussiarouter", sql_pay, use_key)
    data["payments"] = []
    for r in pay_rows:
        pp = r.split("\x1f")
        if len(pp) >= 8:
            data["payments"].append({
                "id": pp[0],
                "provider": pp[1],
                "display_amount": pp[2],
                "display_currency": pp[3],
                "credited_usd_amount": pp[4],
                "status": pp[5],
                "created_at_utc": pp[6],
                "confirmed_at_utc": pp[7],
            })

    # 5. Usage summary
    sql_usage_summary = f"SELECT count(*), coalesce(sum(cost_amount), 0), min(created_at_utc), max(created_at_utc) FROM billing.usage_records WHERE user_id = '{user_id}';"
    usage_rows = run_remote_sql(ssh_host, ssh_password, "openrussiarouter", sql_usage_summary, use_key)
    if usage_rows:
        up = usage_rows[0].split("\x1f")
        if len(up) >= 4:
            data["usage_summary"] = {
                "total_requests": up[0],
                "total_spent_usd": up[1],
                "first_request_at_utc": up[2],
                "last_request_at_utc": up[3],
            }

    # 6. Distinct ID in orr_analytics
    if data["payments"]:
        first_pay_id = data["payments"][0]["id"]
        sql_distinct = f"SELECT distinct_id FROM analytics.events WHERE dedup_key = '{first_pay_id}' LIMIT 1;"
        dist_rows = run_remote_sql(ssh_host, ssh_password, "orr_analytics", sql_distinct, use_key)
        if dist_rows:
            distinct_id = dist_rows[0].split("\x1f")[0]
            data["analytics_distinct_id"] = distinct_id

            sql_events = f"SELECT event_name, occurred_at_utc, properties FROM analytics.events WHERE distinct_id = '{distinct_id}' ORDER BY occurred_at_utc ASC;"
            event_rows = run_remote_sql(ssh_host, ssh_password, "orr_analytics", sql_events, use_key)
            data["product_events"] = []
            for er in event_rows:
                ep = er.split("\x1f")
                if len(ep) >= 3:
                    data["product_events"].append({
                        "event_name": ep[0],
                        "occurred_at_utc": ep[1],
                        "properties": ep[2],
                    })

    return data


def fetch_metrika_data(token, counter_id, reg_time_utc, first_payment_time_utc=None):
    """Fetches visits and goals for the user based on registration or payment timestamp."""
    reg_dt = datetime.fromisoformat(reg_time_utc.replace("+00", "+00:00"))
    date_str = reg_dt.strftime("%Y-%m-%d")

    filter_expr = "ym:s:goal597470019reaches > 0" if first_payment_time_utc else "ym:s:goal597469764reaches > 0"
    params = {
        "ids": counter_id,
        "date1": date_str,
        "date2": datetime.now(timezone.utc).strftime("%Y-%m-%d"),
        "dimensions": "ym:s:dateTime,ym:s:clientID,ym:s:lastTrafficSource,ym:s:UTMSource,ym:s:UTMCampaign,ym:s:startURL,ym:s:deviceCategory,ym:s:operatingSystemRoot,ym:s:browser",
        "metrics": "ym:s:goal597469764reaches,ym:s:goal597545286reaches,ym:s:goal597470019reaches,ym:s:visits,ym:s:pageviews",
        "filters": filter_expr,
        "limit": 50,
    }
    url = "https://api-metrika.yandex.net/stat/v1/data?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"Authorization": f"OAuth {token}"})

    matching_client_id = None
    traffic_info = {}
    with urllib.request.urlopen(req) as resp:
        res = json.loads(resp.read().decode())
        best_diff = None
        target_ts = datetime.fromisoformat((first_payment_time_utc or reg_time_utc).replace("+00", "+00:00")).timestamp()

        for row in res.get("data", []):
            dims = [d["name"] for d in row["dimensions"]]
            try:
                # Metrika timestamp is MSK (UTC+3)
                v_dt = datetime.strptime(dims[0], "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc).timestamp() - (3 * 3600)
                diff = abs(v_dt - target_ts)
            except Exception:
                diff = 0

            if best_diff is None or diff < best_diff:
                best_diff = diff
                matching_client_id = dims[1]
                traffic_info = {
                    "client_id": dims[1],
                    "first_visit_dt": dims[0],
                    "traffic_source": dims[2],
                    "utm_source": dims[3],
                    "utm_campaign": dims[4],
                    "start_url": dims[5],
                    "device": dims[6],
                    "os": dims[7],
                    "browser": dims[8],
                    "goals": {
                        "signup": row["metrics"][0] > 0,
                        "copy_api_key": row["metrics"][1] > 0,
                        "topup_completed": row["metrics"][2] > 0,
                    },
                }

    if not matching_client_id:
        return None

    sessions_params = {
        "ids": counter_id,
        "date1": date_str,
        "date2": datetime.now(timezone.utc).strftime("%Y-%m-%d"),
        "dimensions": "ym:s:dateTime,ym:s:startURL,ym:s:endURL",
        "metrics": "ym:s:visits,ym:s:pageviews,ym:s:goal597545286reaches",
        "filters": f"ym:s:clientID=='{matching_client_id}'",
        "sort": "ym:s:dateTime",
    }
    s_url = "https://api-metrika.yandex.net/stat/v1/data?" + urllib.parse.urlencode(sessions_params)
    s_req = urllib.request.Request(s_url, headers={"Authorization": f"OAuth {token}"})
    sessions = []
    with urllib.request.urlopen(s_req) as resp:
        s_res = json.loads(resp.read().decode())
        for row in s_res.get("data", []):
            dims = [d["name"] for d in row["dimensions"]]
            sessions.append({
                "dateTime": dims[0],
                "start_url": dims[1],
                "end_url": dims[2],
                "pageviews": row["metrics"][1],
                "copied_key": row["metrics"][2] > 0,
            })

    traffic_info["sessions"] = sessions
    return traffic_info


def main():
    parser = argparse.ArgumentParser(description="User Lifecycle & Journey Inspector")
    parser.add_argument("--email", required=True, help="User email")
    parser.add_argument("--ssh-host", default=DEFAULT_SSH_HOST, help="SSH host (default: root@89.191.225.228)")
    parser.add_argument("--metrika-token", help="Yandex OAuth Token (or set YANDEX_OAUTH_TOKEN env)")
    parser.add_argument("--metrika-token-file", help="Path to token file")
    parser.add_argument("--metrika-counter", default=DEFAULT_METRIKA_COUNTER, help="Yandex Metrika Counter ID")
    parser.add_argument("--json", action="store_true", help="Output raw JSON")
    args = parser.parse_args()

    # Check SSH access mode
    use_key = is_ssh_key_working(args.ssh_host)
    ssh_password = None

    if not use_key:
        ssh_password = os.environ.get("SSH_PASSWORD")
        if not ssh_password:
            import getpass
            ssh_password = getpass.getpass(f"Enter SSH Password for {args.ssh_host} (will not be stored): ")

    print(f"[*] Querying PostgreSQL for {args.email} on {args.ssh_host} (Auth: {'SSH Key' if use_key else 'Password'})...")
    db_data = fetch_db_data(args.ssh_host, ssh_password, args.email, use_key=use_key)
    if not db_data:
        print(f"[-] User {args.email} not found in database.")
        sys.exit(1)

    print(f"[+] Found User ID: {db_data['user']['id']}")

    token = resolve_yandex_token(args.metrika_token, args.metrika_token_file)
    metrika_data = None
    if token:
        try:
            print(f"[*] Querying Yandex Metrika API (Counter {args.metrika_counter})...")
            first_pay_time = db_data["payments"][0]["confirmed_at_utc"] if db_data["payments"] else None
            metrika_data = fetch_metrika_data(token, args.metrika_counter, db_data["user"]["created_at_utc"], first_pay_time)
            if metrika_data:
                print(f"[+] Correlated Yandex Metrika Client ID: {metrika_data['client_id']}")
        except Exception as e:
            print(f"[!] Warning: Metrika query failed: {e}")
    else:
        print("[!] Note: Yandex OAuth token not found. Skipping Metrika web-journey (set YANDEX_OAUTH_TOKEN to enable).")

    result = {
        "db": db_data,
        "metrika": metrika_data,
    }

    if args.json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        # Human-friendly report
        print("\n" + "=" * 60)
        print(f"ПОЛНЫЙ ПУТЬ ПОЛЬЗОВАТЕЛЯ: {args.email}")
        print("=" * 60)
        u = db_data["user"]
        print(f"• User ID:         {u['id']}")
        print(f"• Статус аккаунта: {u['status']} (Роль: {u['role']})")
        print(f"• Зарегистрирован: {u['created_at_utc']}")

        if db_data.get("balance"):
            print(f"• Текущий баланс:  ${float(db_data['balance']['amount']):.4f} USD")

        if metrika_data:
            print("\n[МАРКЕТИНГ И ПРИВЛЕЧЕНИЕ (ЯНДЕКС МЕТРИКА)]")
            print(f"• Client ID:       {metrika_data['client_id']}")
            print(f"• Первый визит:    {metrika_data['first_visit_dt']}")
            print(f"• Источник:        {metrika_data['traffic_source']} (Кампания: {metrika_data.get('utm_campaign')})")
            print(f"• Устройство:      {metrika_data['device']}, {metrika_data['os']}, {metrika_data['browser']}")
            print(f"• Посадочная:      {metrika_data['start_url']}")
            goals = metrika_data.get("goals", {})
            print(f"• Цели: Регистрация: {'ДА' if goals.get('signup') else 'НЕТ'} | "
                  f"Скопировал ключ: {'ДА' if goals.get('copy_api_key') else 'НЕТ'} | "
                  f"Оплата: {'ДА' if goals.get('topup_completed') else 'НЕТ'}")

        if db_data.get("api_keys"):
            print("\n[API КЛЮЧИ]")
            for k in db_data["api_keys"]:
                print(f"• Ключ '{k['name']}' (префикс {k['prefix']}): создан {k['created_at_utc']}, последний запрос: {k['last_used_at_utc'] or 'никогда'}")

        if db_data.get("payments"):
            print("\n[ПЛАТЕЖИ]")
            for p in db_data["payments"]:
                print(f"• {p['confirmed_at_utc']}: {p['display_amount']} {p['display_currency']} -> +${float(p['credited_usd_amount']):.4f} USD ({p['provider']}, статус: {p['status']})")

        if db_data.get("usage_summary"):
            us = db_data["usage_summary"]
            print("\n[ИСПОЛЬЗОВАНИЕ API]")
            print(f"• Всего вызовов:   {us['total_requests']}")
            print(f"• Всего потрачено: ${float(us['total_spent_usd']):.4f} USD")
            print(f"• Первый запрос:   {us['first_request_at_utc']}")
            print(f"• Крайний запрос:  {us['last_request_at_utc']}")
        print("=" * 60)


if __name__ == "__main__":
    main()
