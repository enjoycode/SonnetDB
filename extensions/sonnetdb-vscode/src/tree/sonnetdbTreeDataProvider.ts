import * as vscode from 'vscode';
import { SonnetDbConnectionProfile } from '../core/types';

type TreeNode =
  | { kind: 'empty' }
  | { kind: 'connection'; profile: SonnetDbConnectionProfile };

export class SonnetDbTreeDataProvider implements vscode.TreeDataProvider<TreeNode> {
  private readonly emitter = new vscode.EventEmitter<TreeNode | undefined | null | void>();

  public readonly onDidChangeTreeData = this.emitter.onDidChangeTreeData;

  public constructor(
    private readonly getProfiles: () => SonnetDbConnectionProfile[],
  ) {}

  public refresh(): void {
    this.emitter.fire();
  }

  public getTreeItem(element: TreeNode): vscode.TreeItem {
    if (element.kind === 'empty') {
      const item = new vscode.TreeItem('No SonnetDB connections', vscode.TreeItemCollapsibleState.None);
      item.command = {
        command: 'sonnetdb.addConnection',
        title: 'Add Connection',
      };
      item.contextValue = 'empty';
      return item;
    }

    const item = new vscode.TreeItem(element.profile.label, vscode.TreeItemCollapsibleState.None);
    item.description = element.profile.baseUrl;
    item.contextValue = 'connection';
    return item;
  }

  public getChildren(element?: TreeNode): TreeNode[] {
    if (element) {
      return [];
    }

    const profiles = this.getProfiles();
    if (profiles.length === 0) {
      return [{ kind: 'empty' }];
    }

    return profiles.map((profile) => ({
      kind: 'connection',
      profile,
    }));
  }
}
