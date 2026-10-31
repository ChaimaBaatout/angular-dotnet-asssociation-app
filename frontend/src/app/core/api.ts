import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import {
  PagedResult,
  PermissionInfo,
  Profile,
  ProfileOption,
  QrCodeResponse,
  QrCodeStatus,
  SaveProfileRequest,
  SaveUserRequest,
  User,
  UserQuery,
} from './models';

@Injectable({ providedIn: 'root' })
export class QrCodeApi {
  private readonly http = inject(HttpClient);

  status() {
    return this.http.get<QrCodeStatus>('/api/me/qr-code');
  }

  create(currentPassword: string) {
    return this.http.post<QrCodeResponse>('/api/me/qr-code', { currentPassword });
  }

  delete() {
    return this.http.delete<void>('/api/me/qr-code');
  }
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(query: UserQuery) {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search) params = params.set('search', query.search);
    if (query.profileId != null) params = params.set('profileId', query.profileId);
    if (query.isActive != null) params = params.set('isActive', query.isActive);
    return this.http.get<PagedResult<User>>('/api/users', { params });
  }

  get(id: number) {
    return this.http.get<User>(`/api/users/${id}`);
  }

  create(request: SaveUserRequest) {
    return this.http.post<{ user: User; temporaryPassword: string }>('/api/users', request);
  }

  update(id: number, request: SaveUserRequest) {
    return this.http.put<User>(`/api/users/${id}`, request);
  }

  resetPassword(id: number) {
    return this.http.post<{ temporaryPassword: string }>(`/api/users/${id}/reset-password`, null);
  }

  delete(id: number) {
    return this.http.delete<void>(`/api/users/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class ProfilesApi {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<Profile[]>('/api/profiles');
  }

  options() {
    return this.http.get<ProfileOption[]>('/api/profiles/options');
  }

  permissions() {
    return this.http.get<PermissionInfo[]>('/api/profiles/permissions');
  }

  get(id: number) {
    return this.http.get<Profile>(`/api/profiles/${id}`);
  }

  create(request: SaveProfileRequest) {
    return this.http.post<Profile>('/api/profiles', request);
  }

  update(id: number, request: SaveProfileRequest) {
    return this.http.put<Profile>(`/api/profiles/${id}`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`/api/profiles/${id}`);
  }
}
