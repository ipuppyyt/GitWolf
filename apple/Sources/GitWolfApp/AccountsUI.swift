import AppKit
import GitWolfCore
import SwiftUI

/// Flat, single-colour tile standing in for a provider logo.
struct ProviderTile: View {
    let kind: ProviderKind
    var size: CGFloat = 30

    private var color: Color {
        switch kind {
        case .github: Color(red: 0.14, green: 0.16, blue: 0.20)
        case .gitlab: Color(red: 0.89, green: 0.35, blue: 0.14)
        case .bitbucket: Color(red: 0.15, green: 0.45, blue: 0.90)
        case .gitea: Color(red: 0.30, green: 0.62, blue: 0.20)
        case .azure: Color(red: 0.0, green: 0.47, blue: 0.83)
        }
    }
    @Environment(\.colorScheme) private var scheme

    /// GitHub's mark is black, so on dark appearance it sits on a light tile to stay visible.
    private var lightTile: Bool { kind == .github && scheme == .dark }

    var body: some View {
        let hex = lightTile ? "#14161A" : "#FFFFFF"
        RoundedRectangle(cornerRadius: size * 0.28, style: .continuous)
            .fill(lightTile ? Color.white : color)
            .frame(width: size, height: size)
            .overlay {
                if let image = ProviderLogo.image(kind, hex: hex) {
                    Image(nsImage: image).resizable().scaledToFit().padding(size * 0.2)
                }
            }
    }
}

// MARK: - Settings pane

struct AccountsSettings: View {
    private var store = AccountStore.shared
    @State private var adding = false
    @State private var removing: ProviderAccount?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 8) {
                Text("CONNECTED ACCOUNTS")
                    .font(.system(size: 10.5, weight: .semibold)).tracking(0.6)
                    .foregroundStyle(.secondary).padding(.horizontal, 4)
                VStack(spacing: 0) {
                    if store.accounts.isEmpty {
                        Text("No accounts yet. Add one to clone private repositories and sync with your host.")
                            .font(.callout).foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading).padding(14)
                    }
                    ForEach(Array(store.accounts.enumerated()), id: \.element.id) { index, account in
                        if index > 0 { Divider().padding(.leading, 54) }
                        HStack(spacing: 12) {
                            ProviderTile(kind: account.kind)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(account.kind.title).fontWeight(.medium)
                                Text(account.username.isEmpty ? account.host : "@\(account.username) · \(account.host)")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            Button("Remove") { removing = account }
                                .buttonStyle(.plain).font(.callout).pill()
                        }
                        .padding(.horizontal, 12).padding(.vertical, 9)
                    }
                }
                .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.primary.opacity(0.08)))
            }

            HStack {
                Button { adding = true } label: {
                    Label("Add Account…", systemImage: "plus").font(.callout.weight(.medium))
                }
                .buttonStyle(.plain).pill()
                Spacer()
            }

            Text("Tokens are stored in your macOS Keychain and handed to Git only for matching hosts. They are never written into URLs or config files.")
                .font(.caption).foregroundStyle(.secondary)
        }
        .sheet(isPresented: $adding) { AddAccountSheet() }
        .confirmationDialog("Remove this account?", isPresented: Binding(get: { removing != nil }, set: { if !$0 { removing = nil } }),
                            presenting: removing) { account in
            Button("Remove \(account.kind.title) Account", role: .destructive) { store.remove(account) }
        } message: { account in
            Text("The token for \(account.host) will be deleted from the Keychain.")
        }
    }
}

// MARK: - Add account

struct AddAccountSheet: View {
    enum Method: Hashable { case browser, token, password }


    @Environment(\.dismiss) private var dismiss
    @State private var kind = ProviderKind.github
    @State private var host = ProviderKind.github.defaultHost
    @State private var selfHosted = false
    @State private var username = ""
    @State private var token = ""
    @State private var method = Method.browser
    @State private var clientID = ""
    @State private var clientSecret = ""
    @State private var working = false
    @State private var error: String?
    @State private var session = OAuthSession()
    @State private var copiedRedirect = false

    private var cleanHost: String {
        var h = host.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        for prefix in ["https://", "http://"] where h.hasPrefix(prefix) { h.removeFirst(prefix.count) }
        if let at = h.lastIndex(of: "@") { h = String(h[h.index(after: at)...]) }
        while h.hasSuffix("/") { h.removeLast() }
        return h
    }
    private var scheme: String? {
        host.trimmingCharacters(in: .whitespaces).lowercased().hasPrefix("http://") ? "http" : nil
    }
    private var hostHidden: Bool { !selfHosted }
    private var oauthAvailable: Bool { kind.oauthFlow != nil && kind.supportsOAuth(host: cleanHost) }
    private var passwordAllowed: Bool { kind != .github && kind != .gitlab }
    private var effectiveMethod: Method {
        if method == .browser && !oauthAvailable { return .token }
        if method == .password && !passwordAllowed { return oauthAvailable ? .browser : .token }
        return method
    }
    private var needsClientID: Bool { effectiveMethod == .browser && !OAuthClients.hasBuiltIn(host: cleanHost) }
    private var resolvedClientID: String {
        let typed = clientID.trimmingCharacters(in: .whitespaces)
        return typed.isEmpty ? (OAuthClients.clientID(host: cleanHost) ?? "") : typed
    }
    private var canAdd: Bool {
        guard !cleanHost.isEmpty, !working, !session.isActive else { return false }
        switch effectiveMethod {
        case .browser:
            return !resolvedClientID.isEmpty && (!kind.oauthNeedsClientSecret || !clientSecret.trimmingCharacters(in: .whitespaces).isEmpty)
        case .token: return !token.trimmingCharacters(in: .whitespaces).isEmpty
                && (!kind.needsUsername || !username.trimmingCharacters(in: .whitespaces).isEmpty)
        case .password: return !token.trimmingCharacters(in: .whitespaces).isEmpty
                && !username.trimmingCharacters(in: .whitespaces).isEmpty
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Add Account").font(.system(size: 17, weight: .semibold, design: .rounded))

            HStack(spacing: 8) {
                ForEach(ProviderKind.allCases) { item in
                    let selected = kind == item
                    Button {
                        kind = item
                        if !item.hasSelfHosted { selfHosted = false }
                        host = selfHosted ? "" : item.defaultHost
                        error = nil
                    } label: {
                        VStack(spacing: 5) {
                            ProviderTile(kind: item, size: 34)
                            Text(item == .azure ? "Azure DevOps" : item == .gitea ? "Gitea" : item.title)
                                .font(.caption2).lineLimit(1)
                        }
                        .frame(maxWidth: .infinity).padding(.vertical, 8)
                        .background(selected ? Theme.brand.opacity(0.14) : Color.primary.opacity(0.04),
                                    in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .strokeBorder(selected ? Theme.brand.opacity(0.6) : .clear))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .disabled(session.isActive)
                }
            }

            if kind.hasSelfHosted {
                Toggle("Self-hosted server", isOn: Binding(get: { selfHosted }, set: { selfHosted = $0; host = $0 ? "" : kind.defaultHost; error = nil }))
                    .toggleStyle(.switch).controlSize(.small).disabled(session.isActive)
            }

            if !hostHidden {
                field("HOST") {
                    TextField("git.company.com", text: $host).textFieldStyle(.plain)
                        .disabled(session.isActive)
                }
                Text(verbatim: "Your server, e.g. git.company.com or https://git.company.com:8443.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }

            switch effectiveMethod {
            case .browser: browserSection
            case .token, .password: secretSection
            }

            if let reason = kind.oauthUnavailableReason, !oauthAvailable, method == .browser {
                Text(reason).font(.caption).foregroundStyle(.secondary)
            }
            if let error { Text(error).font(.callout).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true) }

            HStack {
                if working {
                    ProgressView().controlSize(.small); Text("Verifying…").font(.caption).foregroundStyle(.secondary)
                } else {
                    Picker("Sign in with", selection: Binding(get: { effectiveMethod }, set: { method = $0; error = nil })) {
                        if oauthAvailable { Text("Browser").tag(Method.browser) }
                        Text("Token").tag(Method.token)
                        if passwordAllowed { Text("Password").tag(Method.password) }
                    }
                    .pickerStyle(.menu).fixedSize().disabled(session.isActive)
                }
                Spacer()
                Button("Cancel", role: .cancel) { session.cancel(); dismiss() }.keyboardShortcut(.cancelAction)
                if effectiveMethod == .browser {
                    Button(kind.oauthFlow == .device ? "Continue" : "Sign In", action: signIn)
                        .keyboardShortcut(.defaultAction).buttonStyle(.borderedProminent).disabled(!canAdd)
                } else {
                    Button("Add", action: add)
                        .keyboardShortcut(.defaultAction).buttonStyle(.borderedProminent).disabled(!canAdd)
                }
            }
        }
        .padding(22)
        .frame(width: 460)
        .onDisappear { session.cancel() }
    }

    // MARK: Sections

    @ViewBuilder private var browserSection: some View {
        switch session.phase {
        case .device(let code, let url):
            VStack(alignment: .leading, spacing: 10) {
                Text("Enter this code on \(url.host ?? cleanHost)")
                    .font(.system(size: 11, weight: .semibold)).tracking(0.4).foregroundStyle(.secondary)
                HStack {
                    Text(code).font(.system(size: 26, weight: .semibold, design: .monospaced)).textSelection(.enabled)
                    Spacer()
                    Button("Copy") {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(code, forType: .string)
                    }.buttonStyle(.bordered)
                    Button("Open Page") { NSWorkspace.shared.open(url) }.buttonStyle(.bordered)
                }
                .padding(12).background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Waiting for you to approve in the browser… The code is already copied.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        case .browser, .finishing:
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text(session.phase == .finishing ? "Finishing sign-in…" : "Complete the sign-in in your browser…")
                    .font(.callout).foregroundStyle(.secondary)
            }
        case .idle:
            VStack(alignment: .leading, spacing: 8) {
                if needsClientID {
                    field("OAUTH CLIENT ID") {
                        TextField("Paste the application’s Client ID", text: $clientID).textFieldStyle(.plain)
                            .font(.system(.body, design: .monospaced))
                    }
                    Text(OAuthClients.setupHint(kind, host: cleanHost))
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    if kind.oauthNeedsClientSecret {
                        field("CLIENT SECRET") {
                            SecureField("Paste the consumer’s Secret", text: $clientSecret).textFieldStyle(.plain)
                        }
                    }
                    if kind.oauthFlow == .pkce {
                        field("REDIRECT URI") {
                            HStack {
                                Text(OAuthLoopback.redirectURI)
                                    .font(.system(.callout, design: .monospaced)).textSelection(.enabled)
                                Spacer(minLength: 8)
                                Button { copyRedirect() } label: {
                                    Label(copiedRedirect ? "Copied" : "Copy", systemImage: copiedRedirect ? "checkmark" : "doc.on.doc")
                                        .labelStyle(.titleAndIcon).font(.caption.weight(.medium))
                                }
                                .buttonStyle(.plain).foregroundStyle(Theme.brand)
                            }
                        }
                    }
                } else {
                    Text("You’ll approve GitWolf in your browser. No token to copy.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }

    @ViewBuilder private var secretSection: some View {
        let password = effectiveMethod == .password
        if kind.needsUsername || password {
            field("USERNAME") { TextField("username", text: $username).textFieldStyle(.plain) }
        }
        field(password ? "PASSWORD" : kind.secretName.uppercased()) {
            SecureField("Paste here", text: $token).textFieldStyle(.plain).onSubmit(add)
        }
        VStack(alignment: .leading, spacing: 4) {
            Text(password ? "Some hosts no longer accept passwords for Git over HTTPS; use a token there."
                          : kind.scopeHint).font(.caption).foregroundStyle(.secondary)
            if !password, let link = kind.tokenPageURL(host: cleanHost) {
                Button("Create a \(kind.secretName.lowercased()) on \(cleanHost) ↗") { NSWorkspace.shared.open(link) }
                    .buttonStyle(.link).font(.caption)
            }
        }
    }

    private func field<C: View>(_ label: String, @ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.system(size: 11, weight: .semibold)).tracking(0.4).foregroundStyle(.secondary)
            content()
                .padding(.horizontal, 12).padding(.vertical, 8)
                .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
    }

    // MARK: Actions

    private func copyRedirect() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(OAuthLoopback.redirectURI, forType: .string)
        copiedRedirect = true
        Task {
            try? await Task.sleep(for: .seconds(2))
            copiedRedirect = false
        }
    }

    private func signIn() {
        guard canAdd else { return }
        error = nil
        let id = resolvedClientID
        let account = ProviderAccount(kind: kind, host: cleanHost, username: "", scheme: scheme, oauthClientID: id)
        session.start(account: account, clientID: id, clientSecret: kind.oauthNeedsClientSecret ? clientSecret.trimmingCharacters(in: .whitespaces) : nil) { account, tokens in
            Task { await finishOAuth(account, tokens) }
        } failed: { message in
            error = message
        }
    }

    private func finishOAuth(_ account: ProviderAccount, _ tokens: OAuthTokens) async {
        working = true
        var account = account
        do {
            account.username = try await ProviderClient(account: account, token: tokens.accessToken).currentUser()
            if needsClientID { OAuthClients.remember(account.oauthClientID ?? "", host: account.host) }
            try AccountStore.shared.addOAuth(account, tokens: tokens)
            dismiss()
        } catch {
            self.error = error.localizedDescription
            working = false
        }
    }

    private func add() {
        guard canAdd, effectiveMethod != .browser else { return }
        working = true
        error = nil
        let secret = token.trimmingCharacters(in: .whitespacesAndNewlines)
        let password = effectiveMethod == .password
        var account = ProviderAccount(kind: kind, host: cleanHost, username: username.trimmingCharacters(in: .whitespaces),
                                      usesPassword: password, scheme: scheme)
        Task {
            do {
                if kind.supportsRepositoryListing {
                    let login = try await ProviderClient(account: account, token: secret).currentUser()
                    if !kind.needsUsername && !password { account.username = login }
                } else if account.username.isEmpty {
                    account.username = "git"
                }
                try AccountStore.shared.add(account, token: secret)
                dismiss()
            } catch {
                self.error = error.localizedDescription
                working = false
            }
        }
    }
}

private extension ProviderKind {
    var hasSelfHosted: Bool { self != .azure }
}
